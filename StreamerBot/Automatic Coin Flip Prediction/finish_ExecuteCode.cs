using System;

public class CPHInline
{
	public bool Execute()
	{
		if(!CPH.TryGetArg("prediction.Id", out string id)) return false;
		if(!CPH.TryGetArg("prediction.outcome0.id", out string resId0)) return false;
		if(!CPH.TryGetArg("prediction.outcome1.id", out string resId1)) return false;
		string[] outcomes = new[]{resId0, resId1};
		
		Random rnd = new Random();
		int res = rnd.Next(2);
		
		CPH.TwitchPredictionResolve(id, outcomes[res]);
		CPH.SetGlobalVar("_coinFlipId", "null", false);
		
		return true;
	}
}
