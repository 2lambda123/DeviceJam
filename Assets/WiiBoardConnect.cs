using System;
using UnityEngine;
using System.Collections;

public class PlayerSpawner : MonoBehaviour {
	public Transform playerPrefab;
	public ArrayList playerScripts = new ArrayList();
 
	void OnServerInitialized() {
    	SpawnPlayer(Network.player);
	}
	
	void OnPlayerConnected(NetworkPlayer player) {
    	SpawnPlayer(player);
	}
	
	void SpawnPlayer(NetworkPlayer player)
	{
    	string tempPlayerString = player.ToString();
    	int playerNumber = Convert.ToInt32(tempPlayerString);
		Transform newPlayerTransform = (Transform)Network.Instantiate(playerPrefab, transform.position, transform.rotation, playerNumber);
		NetworkView theNetworkView = newPlayerTransform.networkView;
		
		playerScripts.Add(newPlayerTransform.GetComponent("PlayerMoveAuthoritative"));
		theNetworkView.RPC("SetPlayer", RPCMode.AllBuffered, player);
	}
		
	// Use this for initialization
	void Start () {
	
	}
	
	// Update is called once per frame
	void Update () {
	
	}
}
