using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using System.Net.Http.Headers;

// Based on original from LeBluxTV
public class ObjectData
{
    public KeyValue TwitchOAuth { get; set; }
}

public class KeyValue
{
    public string accessToken { get; set; }
}

public class Root
{
    public int Total { get; set; }

    public List<Datum> Data { get; set; }

    public Pagination Pagination { get; set; }
}

public class Datum
{
    public string User_id { get; set; } //id

    public string User_login { get; set; } //UserName

    public string User_name { get; set; } //displayName

    public DateTime Followed_at { get; set; }
}

public class Pagination
{
    public string Cursor { get; set; }
}

public class FollowersDatas
{
    public string Id { get; set; }

    public string UserName { get; set; }

    public string DisplayName { get; set; }

    public DateTime Since { get; set; }
}

public class AllDatas
{
    public int TotalNb { get; set; }

    public List<FollowersDatas> FollowersDatas { get; set; }

    public string TheCursor { get; set; }
}

public class CPHInline
{
    private string broadcastUserId;
    private IDictionary<string, string> currentFollowers;
    private HttpClient client;
    
    private bool inited = false;
    
    public void Init()
    {
		currentFollowers = new Dictionary<string, string>();
		client = new HttpClient();
		
		CPH.RegisterCustomTrigger("Unfollow", "userUnfollowed_live", new []{"Twitch", "Channel"});
		CPH.RegisterCustomTrigger("Unfollow (Offline)", "userUnfollowed_offline", new []{"Twitch", "Channel"});
    }
    
    public bool Execute() {
    	if(inited) return true;
    	
    	FetchFollowerList();
		UpdateFollowerList();
		
		inited = true;
    	
    	return true;
    }
    
    public void Dispose()
    {
    	SaveFollowerList();
    	
    	currentFollowers?.Clear();
    	client?.Dispose();
    }
    
    public bool FetchFollowers()
    {
        if(!CPH.TryGetArg<string>("broadcastUserId", out broadcastUserId)) return false;
        if(!CPH.TryGetArg("autoSave", out bool autoSave)) autoSave = false;
        
        Task<AllDatas> getAllDatasTask = FunctionGetAllDatas();
        getAllDatasTask.Wait();
        AllDatas datas = getAllDatasTask.Result;
        
        currentFollowers.Clear();
        
        for (int i = 0; i < (datas.TotalNb - 1); i++)
        {
            string userId = datas.FollowersDatas[i].Id;
            if(String.IsNullOrEmpty(userId)) continue;
            string userName = datas.FollowersDatas[i].UserName;
            if(String.IsNullOrEmpty(userName)) continue;
            currentFollowers.Add(userId, userName);
        }
        
        if(autoSave) return SaveFollowerList();
		
        return true;
    }
    
    public bool SaveFollowerList()
    {
    	CPH.SetGlobalVar("followersList", currentFollowers, true);
        return true;
    }
    
    public bool FetchFollowerList()
    {
    	IDictionary<string, string>? fetched = CPH.GetGlobalVar<IDictionary<string, string>>("followersList", true);
    	if(fetched != null) currentFollowers = fetched;
    	else currentFollowers.Clear();
    	return true;
    }
    
    public bool ClearFollowerList()
    {
    	CPH.SetGlobalVar("followersList", null, true);
    	currentFollowers.Clear();
    	return true;
    }
    
    public bool AddFollower() {
    	if(!CPH.TryGetArg("userId", out string uid)) return false;
    	if(!CPH.TryGetArg("userName", out string name)) return false;
    	
    	if(!currentFollowers.ContainsKey(uid)) currentFollowers.Add(uid, name);
    	return true;
    }
    
    public bool UpdateFollowerList()
    {
		// Store currently known follower list
		IDictionary<string, string> oldFollowers = new Dictionary<string, string>(currentFollowers);
		
		// Fetch session storage of unfollows to prevent dupes
		IDictionary<string, string> lostFollowers = new Dictionary<string, string>();
		IDictionary<string, string>? storedTemp = CPH.GetGlobalVar<IDictionary<string, string>>("lostFollowers", false);
		if(storedTemp != null) lostFollowers = storedTemp;
		storedTemp = null; // Free up reference
		
		// Fetch actual current follower list from Twitch
		FetchFollowers();
		
		foreach(KeyValuePair<string, string> entry in oldFollowers) {
			if(!currentFollowers.ContainsKey(entry.Key) && !lostFollowers.ContainsKey(entry.Key)) {
				// Lost follower since last check
				lostFollowers.Add(entry.Key, entry.Value);
				
				// Add to credits
				CPH.AddToCredits("unfollows", entry.Value, false);
				
				// Trigger "User Unfollowed" event with userId and userName args
				Dictionary<string, object> evArgs = new Dictionary<string, object>();
				evArgs.Add("userId", entry.Key);
				evArgs.Add("userName", entry.Value);
				
				if(inited) CPH.TriggerCodeEvent("userUnfollowed_live", evArgs);
				else CPH.TriggerCodeEvent("userUnfollowed_offline", evArgs);
			}
		}
		
		CPH.SetGlobalVar("lostFollowers", lostFollowers, false);
		
		return true;
    }

    private async Task<Root> FunctionCallTwitchAPI(string cursor)
    {
        string to_id = broadcastUserId;
        string tokenValue = CPH.TwitchOAuthToken;
        string clientIdValue = CPH.TwitchClientId;
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("client-ID", clientIdValue);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenValue);
        HttpResponseMessage response = await client.GetAsync("https://api.twitch.tv/helix/channels/followers?broadcaster_id=" + to_id + "&first=100&after=" + cursor);
        HttpContent responseContent = response.Content;
        string responseBody = await response.Content.ReadAsStringAsync();
        Root root = JsonConvert.DeserializeObject<Root>(responseBody);
        return root;
    }

    private async Task<AllDatas> FunctionGetAllDatas()
    {
        string cursor = null;
        AllDatas datas = new AllDatas()
        {FollowersDatas = new List<FollowersDatas>(), TotalNb = new int ()};
        do
        {
            Root root = await FunctionCallTwitchAPI(cursor);
            foreach (Datum datum in root.Data)
            {
                FollowersDatas newData = new FollowersDatas{Id = datum.User_id, DisplayName = datum.User_name, UserName = datum.User_login, Since = datum.Followed_at};
                datas.FollowersDatas.Add(newData);
            }

            datas.TotalNb = root.Total;
            cursor = root.Pagination.Cursor;
        }
        while (cursor != null);
        return datas;
    }
}