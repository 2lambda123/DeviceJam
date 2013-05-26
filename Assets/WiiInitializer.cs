using System;
using UnityEngine;
using System.Collections;

public class WiiInitializer : MonoBehaviour {
	public Transform playerCharacter;
	static private Transform playerTransform;
	public ArrayList playerScripts = new ArrayList();
	
	void OnServerInitialized()
	{
    	SpawnPlayer(Network.player);
		playerTransform.networkView.RPC("Enable", RPCMode.AllBuffered, Network.player);
	}
	
	void OnPlayerConnected(NetworkPlayer player)
	{
    	playerTransform.networkView.RPC("Enable", RPCMode.AllBuffered, player);
	}
	
	void SpawnPlayer(NetworkPlayer player)
	{
		playerTransform = (Transform)Network.Instantiate(playerCharacter, transform.position, transform.rotation, 0);
		playerScripts.Add(playerTransform.GetComponent("OVRPlayerController"));
	}
}
