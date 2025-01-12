using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class CPHInline
{
	private List<JObject> backlog = new List<JObject>();
	private int maxLogLength = 50;
	private bool doCache = false;
	
	private List<string> clients = new List<string>();
	
	private bool inited = false;
	
	public bool Execute()
	{
		if(!CPH.TryGetArg<bool>("useGlobalCache", out doCache)) doCache = false;
		if(doCache && !inited) {
			//Only on first start do we load the cache, not on any subsequent setting change.
			List<JObject>? cache = CPH.GetGlobalVar<List<JObject>>("chatBacklogCache", false);
			if(cache != null) backlog = cache;
		}
		
		if(!CPH.TryGetArg<int>("maxLogLength", out maxLogLength)) maxLogLength = 50;
		if(backlog.Count > maxLogLength) backlog.RemoveRange(maxLogLength, backlog.Count - maxLogLength);
		
		inited = true;
		return true;
	}
	
	public void Dispose() {
		if(doCache) {
			CPH.SetGlobalVar("useGlobalCache", backlog, false);
		}
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
	
	public bool onMessage() {
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
		
		JObject chatMessage = new JObject(
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
			foreach(Twitch.Common.Models.Emote e in emotes) {
				int start = e.StartIndex;
				int end = e.EndIndex;
				string name = e.Name;
				string url = e.ImageUrl;
				
				JObject emote = new JObject(
					new JProperty("name", name),
					new JProperty("url", url),
					new JProperty("start", start),
					new JProperty("end", end)
				);
				
				((JArray)chatMessage["emotes"]).Add(emote);
			}
		}
		
		backlog.Add(chatMessage);
		if(backlog.Count > maxLogLength) backlog.RemoveRange(maxLogLength, backlog.Count - maxLogLength);
		sendMessage(chatMessage);
		
		return true;
	}
	
	public bool onDelete() {
		if(!CPH.TryGetArg("targetMessageId", out string id)) return false;
		
		for(int i = 0; i < backlog.Count; i++) {
			JObject m = backlog[i];
			if((string)m["msgId"] == id) {
				backlog.RemoveAt(i);
				break;
			}
		}
		
		sendDelete(id);
		
		return true;
	}
	
	public bool onClear() {
		backlog.Clear();
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatClear", null);
		}
		
		return true;
	}
	
	public bool onBan() {
		if(!CPH.TryGetArg("userId", out string userId)) return false;
		if(!CPH.TryGetArg("user", out string user)) return false;
		
		for(int i = 0; i < backlog.Count; i++) {
			JObject m = backlog[i];
			if((string)m["userId"] == userId) {
				backlog.RemoveAt(i);
				continue;
			}
		}
		
		sendBan(userId, user);
		
		return true;
	}
	
	private async Task sendMessage(JObject message) {
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatMessage", message);
		}
	}
	
	private async Task sendDelete(string id) {
		JValue idJson = new JValue(id);
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "deleteMessage", idJson);
		}
	}
	
	private async Task sendBan(string userId, string user) {
		JObject banJson = new JObject(
			new JProperty("userId", userId),
			new JProperty("user", user)
		);
		
		foreach(string cid in clients) {
			broadcastMessage(cid, "chatBan", banJson);
		}
	}
	
	private async Task sendBacklog(string cid) {
		JArray logJson = new JArray();
		foreach(JObject message in backlog) {
			logJson.Add(message);
		}
		
		await broadcastMessage(cid, "backlog", logJson);
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
}