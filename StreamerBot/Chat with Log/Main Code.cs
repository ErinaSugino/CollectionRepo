using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class CPHInline
{
	private List<JObject> backlog = new List<JObject>();
	private List<JObject> pendingGigaEmotes = new List<JObject>();
	private List<string> twitchDefaultColors = new List<string>{"#FF4A80","#FF7070","#FA8E4B","#FEE440","#5FFF77","#00F5D4","#00BBF9","#4371FB","#9B5DE5","#F670DD"};
	private Dictionary<string, string> userColorCache = new Dictionary<string, string>();
	private int maxLogLength = 50;
	private bool doCache = false;
	
	private List<string> clients = new List<string>();
	
	private bool inited = false;
	
	public void Init() {
		verifyWebSocket().GetAwaiter().GetResult();
	}
	
	public bool Execute()
	{
		if(!CPH.TryGetArg<bool>("useGlobalCache", out doCache)) doCache = false;
		if(doCache && !inited) {
			//Only on first start do we load the cache, not on any subsequent setting change.
			List<JObject>? cache = CPH.GetGlobalVar<List<JObject>>("chatBacklogCache", false);
			if(cache != null) backlog = cache;
		}
		
		if(!CPH.TryGetArg<int>("maxLogLength", out maxLogLength)) maxLogLength = 50;
		if(backlog.Count > maxLogLength) backlog.RemoveRange(0, backlog.Count - maxLogLength);
		
		inited = true;
		return true;
	}
	
	public void Dispose() {
		if(doCache) {
			CPH.SetGlobalVar("useGlobalCache", backlog, false);
		}
		backlog.Clear();
		pendingGigaEmotes.Clear();
	}
	
	public bool onConnect() {
		if(!CPH.TryGetArg("sessionId", out string cid)) return false;
		if(clients.Contains(cid)) return true;
		
		clients.Add(cid);
		
		sendBacklog(cid);
		
		return true;
	}
	
	public bool onDisconnect() {
		if(!CPH.TryGetArg("sessionId", out string cid)) return false;
		if(!clients.Contains(cid)) return true;
		
		clients.Remove(cid);
		
		return true;
	}
	
	public bool onTwitchMessage() {
		if(!CPH.TryGetArg("msgId", out string msgId)) return false;
		if(!CPH.TryGetArg("rawInput", out string message)) message = null;
		if(!CPH.TryGetArg<List<Twitch.Common.Models.Emote>>("emotes", out List<Twitch.Common.Models.Emote>? emotes)) emotes = null;
		if(!CPH.TryGetArg("userId", out string userId)) userId = null;
		if(!CPH.TryGetArg("user", out string user)) user = null;
		if(!CPH.TryGetArg("color", out string color)) color = null;
		if(!CPH.TryGetArg<List<Twitch.Common.Models.Badge>>("badges", out List<Twitch.Common.Models.Badge>? badges)) badges = null;
		if(!CPH.TryGetArg("triggerName", out string type)) type = "Chat Message";
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		short typeIndex = 0;
		if(type == "Watch Streak") typeIndex = 1;
		else if(type == "Resubscription") typeIndex= 2;
		
		if(color == null) color = fetchDefaultColor(userId);
		
		JObject chatMessage = new JObject(
			new JProperty("source", "twitch"),
			new JProperty("msgId", msgId),
			new JProperty("message", message),
			new JProperty("userId", userId),
			new JProperty("user", user),
			new JProperty("color", color),
			new JProperty("badges", new JArray()),
			new JProperty("emotes", new JArray()),
			new JProperty("timestamp", timestamp),
			new JProperty("type", typeIndex)
		);
		
		if(badges != null) {
			foreach(Twitch.Common.Models.Badge b in badges) {
				string img = b.ImageUrl;
				((JArray)chatMessage["badges"]).Add(img);
			}
		}
		
		if(emotes != null) {
			emotes.Sort((a,b) => a.StartIndex - b.StartIndex);
			
			foreach(Twitch.Common.Models.Emote e in emotes) {
				int start = e.StartIndex;
				int end = e.EndIndex;
				int id = 0;//e.Id;
				string name = e.Name;
				string url = e.ImageUrl;
				string eType = e.Type;
				
				JObject emote = new JObject(
					new JProperty("id", id),
					new JProperty("name", name),
					new JProperty("url", url),
					new JProperty("start", start),
					new JProperty("end", end),
					new JProperty("type", eType),
					new JProperty("big", false)
				);
				
				((JArray)chatMessage["emotes"]).Add(emote);
			}
		}
		
		for(int i = pendingGigaEmotes.Count - 1; i >= 0; i--) {
			JObject giga = pendingGigaEmotes[i];
			int timeout = (int)giga["timeout"];
			timeout -= 1;
			CPH.LogDebug("Waiting for emote - remaining "+timeout.ToString());
			giga["timeout"] = timeout;
			if(timeout < 0) {
				pendingGigaEmotes.RemoveAt(i);
				continue;
			}
			if(giga["userId"].ToString() != userId) continue;
			if(String.Compare(giga["message"].ToString(), message) != 0) continue;
			
			string gigaName = giga["gigaName"].ToString();
			string gigaUrl = giga["gigaUrl"].ToString();
			if(emotes.Count <= 0) continue;
			
			bool found = false;
			JArray parsedEmotes = chatMessage["emotes"] as JArray;
			for(int j = parsedEmotes.Count - 1; j >= 0; j--) {
				JObject e = parsedEmotes[j] as JObject;
				if(String.Compare(e["name"].ToString(), gigaName) != 0) continue;
				
				e["big"] = true;
				if(!String.IsNullOrEmpty(gigaUrl)) e["url"] = gigaUrl;
				
				found = true;
				break;
			}
			
			if(!found) continue;
			
			pendingGigaEmotes.RemoveAt(i);
			break;
		}
		
		backlog.Add(chatMessage);
		if(backlog.Count > maxLogLength) backlog.RemoveRange(0, backlog.Count - maxLogLength);
		sendMessage(chatMessage);
		
		return true;
	}
	
	public bool onYouTubeMessage() {
		if(!CPH.TryGetArg("messageId", out string msgId)) return false;
		if(!CPH.TryGetArg("message", out string message)) message = null;
		if(!CPH.TryGetArg("emotes", out string emotes)) emotes = null;
		if(!CPH.TryGetArg("userId", out string userId)) userId = null;
		if(!CPH.TryGetArg("user", out string user)) user = null;
		if(!CPH.TryGetArg("color", out string color)) color = null;
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		short typeIndex = 0;
		
		if(color == null) color = fetchDefaultColor(userId);
		
		JObject chatMessage = new JObject(
			new JProperty("source", "youtube"),
			new JProperty("msgId", msgId),
			new JProperty("message", message),
			new JProperty("userId", userId),
			new JProperty("user", user),
			new JProperty("color", color),
			new JProperty("badges", null),
			new JProperty("emotes", new JArray()),
			new JProperty("timestamp", timestamp),
			new JProperty("type", typeIndex)
		);
		
		if(emotes != null) {
			JArray jEmotes = JArray.Parse(emotes);
			List<JObject> emoteList = jEmotes.ToObject<List<JObject>>();
			emoteList.Sort((a,b) => (int)a["startIndex"] - (int)b["startIndex"]);
			
			foreach(JObject e in emoteList) {
				int start = (int)e["startIndex"];
				int end = (int)e["endIndex"];
				int id = 0;//e.Id;
				string name = (string)e["name"];
				string url = (string)e["imageUrl"];
				string eType = "youtube";
				
				JObject emote = new JObject(
					new JProperty("id", id),
					new JProperty("name", name),
					new JProperty("url", url),
					new JProperty("start", start),
					new JProperty("end", end),
					new JProperty("type", eType),
					new JProperty("big", false)
				);
				
				((JArray)chatMessage["emotes"]).Add(emote);
			}
		}
		
		backlog.Add(chatMessage);
		if(backlog.Count > maxLogLength) backlog.RemoveRange(0, backlog.Count - maxLogLength);
		sendMessage(chatMessage);
		
		return true;
	}
	
	public bool onGigaEmote() {
		if(!CPH.TryGetArg("rawInput", out string message)) return false;
		if(!CPH.TryGetArg("userId", out string userId)) return false;
		if(!CPH.TryGetArg("user", out string user)) user = null;
		if(!CPH.TryGetArg("gigantifiedEmoteId", out string gigaId)) return false;
		if(!CPH.TryGetArg("gigantifiedEmoteName", out string gigaName)) return false;
		if(!CPH.TryGetArg("gigantifiedEmoteUrl", out string gigaUrl)) gigaUrl = null;
		long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		
		bool assignedGiga = false;
		string messageId = null;
		for(int i = backlog.Count - 1; i >= 0; i--) {
			// Reverse loop to check newest messages first
			JObject m = backlog[i];
			if(m["userId"].ToString() != userId) continue;
			if(String.Compare(m["message"].ToString(), message) != 0) continue;
			
			// Should be the right message now
			assignedGiga = true;
			JArray mEmotes = m["emotes"] as JArray;
			if(mEmotes.Count <= 0) break;
			bool foundEmote = false;
			for(int j = mEmotes.Count - 1; j >= 0; j--) {
				JObject e = mEmotes[j] as JObject;
				if(e["name"].ToString() != gigaName) continue;
				e["big"] = true;
				if(!String.IsNullOrEmpty(gigaUrl)) e["url"] = gigaUrl;
				foundEmote = true;
				break;
			}
			if(!foundEmote) break;
			
			messageId = m["msgId"].ToString();
			break;
		}
		
		if(!assignedGiga) {
			JObject gigaData = new JObject(
				new JProperty("userId", userId),
				new JProperty("user", user),
				new JProperty("message", message),
				new JProperty("gigaId", gigaId),
				new JProperty("gigaName", gigaName),
				new JProperty("gigaUrl", gigaUrl),
				new JProperty("timestamp", timestamp),
				new JProperty("timeout", maxLogLength)
			);
			
			pendingGigaEmotes.Add(gigaData);
		} else if(!String.IsNullOrEmpty(messageId)) {
			JObject payload = new JObject(
				new JProperty("userId", userId),
				new JProperty("user", user),
				new JProperty("msgId", messageId),
				new JProperty("gigaId", gigaId),
				new JProperty("gigaName", gigaName),
				new JProperty("gigaUrl", gigaUrl),
				new JProperty("timestamp", timestamp)
			);
			
			sendGigantify(payload);
		}
		
		return true;
	}
	
	public bool onDelete() {
		// Afaik deleting specific messages is only available for Twitch. Atleast I see no trigger for it for YouTube.
		if(!CPH.TryGetArg("targetMessageId", out string id)) return false;
		
		for(int i = backlog.Count-1; i >= 0; i--) {
			JObject m = backlog[i];
			if((string)m["source"] != "twitch") continue;
			if((string)m["msgId"] == id) {
				backlog.RemoveAt(i);
				break;
			}
		}
		
		sendDelete(id, "twitch");
		
		return true;
	}
	
	public bool onClear() {
		if(!CPH.TryGetArg("platform", out string source)) source = null;
		
		if(source == null) backlog.Clear();
		else {
			for(int i = backlog.Count-1; i >= 0; i--) {
				JObject m = backlog[i];
				if((string)m["source"] == source) backlog.RemoveAt(i);
			}
		}
		
		sendClear(source);
		
		return true;
	}
	
	public bool onBan() {
		if(!CPH.TryGetArg("userId", out string userId)) return false;
		if(!CPH.TryGetArg("user", out string user)) return false;
		EventSource source = CPH.GetSource();
		string sourceName = source == EventSource.YouTube ? "youtube" : "twitch";
		
		for(int i = backlog.Count-1; i >= 0; i--) {
			JObject m = backlog[i];
			if((string)m["source"] != sourceName) continue;
			if((string)m["userId"] == userId) {
				backlog.RemoveAt(i);
				continue;
			}
		}
		
		sendBan(userId, user, sourceName);
		
		return true;
	}
	
	private async Task sendMessage(JObject message) {
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatMessage", message);
		}
	}
	
	private async Task sendGigantify(JObject payload) {
		foreach(string cid in clients) {
			broadcastMessage(cid, "gigantify", payload);
		}
	}
	
	private async Task sendDelete(string id, string platform) {
		JObject deleteJson = new JObject(
			new JProperty("msgId", id),
			new JProperty("platform", platform)
		);
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "deleteMessage", deleteJson);
		}
	}
	
	private async Task sendBan(string userId, string user, string platform) {
		JObject banJson = new JObject(
			new JProperty("userId", userId),
			new JProperty("user", user),
			new JProperty("platform", platform)
		);
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatBan", banJson);
		}
	}
	
	private async Task sendClear(string platform) {
		JObject payload = null;
		if(platform != null) payload = new JObject(
			new JProperty("platform", platform)
		);
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatClear", payload);
		}
	}
	
	private async Task sendBacklog(string cid) {
		JArray logJson = new JArray();
		foreach(JObject message in backlog) {
			logJson.Add(message);
		}
		
		await broadcastMessage(cid, "backlog", logJson);
	}
	
	private async Task<bool> verifyWebSocket() {
		int wsId = CPH.WebsocketCustomServerGetConnectionByName("Chat with Log");
		if(wsId == null || wsId < 0) return false;
		if(!CPH.WebsocketCustomServerIsListening(wsId)) CPH.WebsocketCustomServerStart(wsId);
		return true;
	}
	
	private async Task<bool> broadcastMessage(string cid, string ev, JToken data) {
		int wsId = CPH.WebsocketCustomServerGetConnectionByName("Chat with Log");
		if(wsId == null || wsId < 0) return false;
		if(!CPH.WebsocketCustomServerIsListening(wsId)) return false;
		
		JObject payload = new JObject(
			new JProperty("event", ev),
			new JProperty("data", data)
		);
		
		CPH.WebsocketCustomServerBroadcast(payload.ToString(Formatting.None), cid, wsId);
		
		return true;
	}
	
	private string fetchDefaultColor(string userId) {
		if(!userColorCache.ContainsKey(userId)) {
			Random rnd = new Random();
			int i = rnd.Next(twitchDefaultColors.Count);
			userColorCache.Add(userId, twitchDefaultColors[i]);
		}
		
		return userColorCache[userId];
	}
}