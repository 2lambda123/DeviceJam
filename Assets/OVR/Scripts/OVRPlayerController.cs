﻿/************************************************************************************

Filename    :   OVRPlayerController.cs
Content     :   Player controller interface. 
				This script drives OVR camera as well as controls the locomotion
				of the player, and handles physical contact in the world.	
Created     :   January 8, 2013
Authors     :   Peter Giokaris

Copyright   :   Copyright 2013 Oculus VR, Inc. All Rights reserved.

Use of this software is subject to the terms of the Oculus LLC license
agreement provided at the time of installation or download, or which
otherwise accompanies this software in either electronic or hard copy form.

************************************************************************************/

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]

//-------------------------------------------------------------------------------------
// ***** OVRPlayerController
//
// OVRPlayerController implements a basic first person controller for the Rift. It is 
// attached to the OVRPlayerController prefab, which has an OVRCameraController attached
// to it. 
// 
// The controller will interact properly with a Unity scene, provided that the scene has
// collision assigned to it. 
//
// The OVRPlayerController prefab has an empty GameObject attached to it called 
// ForwardDirection. This game object contains the matrix which motor control bases it
// direction on. This game object should also house the body geometry which will be seen
// by the player.
//
public class OVRPlayerController : OVRComponent
{	
	protected CharacterController 	Controller 		 = null;
	protected OVRCameraController 	CameraController = null;
	private bool sendFalse = true;

	public float Acceleration 	   = 0.1f;
	public float Damping 		   = 0.15f;
	public float BackAndSideDampen = 0.5f;
	public float JumpForce 		   = 0.3f;
	public float RotationAmount    = 1.5f;
	public float GravityModifier   = 0.379f;
		
	private float   MoveScale 	   = 1.0f;
	private Vector3 MoveThrottle   = Vector3.zero;
	private float   FallSpeed 	   = 0.0f;

	private bool currentServerMoveForward	= false;
	private bool currentServerMoveLeft 		= false;
	private bool currentServerMoveRight		= false;
	private bool currentServerMoveBack		= false;

	private bool clientMoveForward	= false;
	private bool clientMoveLeft 	= false;
	private bool clientMoveRight	= false;
	private bool clientMoveBack		= false;

	// Initial direction of controller (passed down into CameraController)
	private Quaternion OrientationOffset = Quaternion.identity;			
	// Rotation amount from inputs (passed down into CameraController)
	private float 	YRotation 	 = 0.0f;
	
	// Transfom used to point player in a given direction; 
	// We should attach objects to this if we want them to rotate 
	// separately from the head (i.e. the body)
	protected Transform DirXform = null;
	
	// We can adjust these to influence speed and rotation of player controller
	private float MoveScaleMultiplier     = 1.0f; 
	private float RotationScaleMultiplier = 1.0f; 
	
	//
	// STATIC VARIABLES
	//
	public static bool  AllowMouseRotation      = false;
 	
	// * * * * * * * * * * * * *
	
	[RPC]
	void SendMovementInput(bool mF, bool mL, bool mB, bool mR) 
	{
		if(Network.isServer){
			Debug.Log("Server: " + mF + " " + mL + " " + mB + " " + mR);
    	} else {
    		Debug.Log("Client: " + mF + " " + mL + " " + mB + " " + mR);
    	}
    	currentServerMoveForward	= mF;
		currentServerMoveLeft		= mL;
		currentServerMoveBack		= mB;
		currentServerMoveRight		= mR;
	}
	
	[RPC]
	void Enable(NetworkPlayer player) {
    	if (player == Network.player) {
        	enabled = true;
		}
	}

	[RPC]
	void MyDebug(string log) {
    	Debug.Log(log);
	}

	// Awake
	new public virtual void Awake()
	{
		base.Awake();
		
		// We use Controller to move player around
		Controller = gameObject.GetComponent<CharacterController>();
		
		if(Controller == null)
			Debug.LogWarning("OVRPlayerController: No CharacterController attached.");
					
		// We use OVRCameraController to set rotations to cameras, 
		// and to be influenced by rotation
		OVRCameraController[] CameraControllers;
		CameraControllers = gameObject.GetComponentsInChildren<OVRCameraController>();
		
		if(CameraControllers.Length == 0)
			Debug.LogWarning("OVRPlayerController: No OVRCameraController attached.");
		else if (CameraControllers.Length > 1)
			Debug.LogWarning("OVRPlayerController: More then 1 OVRCameraController attached.");
		else
			CameraController = CameraControllers[0];	
	
		// Instantiate a Transform from the main game object (will be used to 
		// direct the motion of the PlayerController, as well as used to rotate
		// a visible body attached to the controller)
		DirXform = null;
		Transform[] Xforms = gameObject.GetComponentsInChildren<Transform>();
		
		for(int i = 0; i < Xforms.Length; i++)
		{
			if(Xforms[i].name == "ForwardDirection")
			{
				DirXform = Xforms[i];
				break;
			}
		}
		
		if(DirXform == null)
			Debug.LogWarning("OVRPlayerController: ForwardDirection game object not found. Do not use.");
	}

	// Start
	new public virtual void Start()
	{
		base.Start();
		
		InitializeInputs();	
		SetCameras();
	}
		
	// Update 
	new public virtual void Update()
	{
		Debug.Log("Server = " + Network.isServer);
		Debug.Log("Client = " + Network.isClient);
		
		base.Update();
		
		UpdateMovement();

		Vector3 moveDirection = Vector3.zero;
		
		float motorDamp = (1.0f + (Damping * DeltaTime));
		MoveThrottle.x /= motorDamp;
		MoveThrottle.y = (MoveThrottle.y > 0.0f) ? (MoveThrottle.y / motorDamp) : MoveThrottle.y;
		MoveThrottle.z /= motorDamp;

		if (Network.isServer) {
			MyDebug("Server; MoveThrottle = " + MoveThrottle);
		} else if (Network.isClient) {
			networkView.RPC("MyDebug", RPCMode.Server, "Client; MoveThrottle = " + MoveThrottle);
		}



		moveDirection += MoveThrottle * DeltaTime;
		
		// Gravity
		if (Controller.isGrounded && FallSpeed <= 0)
			FallSpeed = ((Physics.gravity.y * (GravityModifier * 0.002f)));	
		else
			FallSpeed += ((Physics.gravity.y * (GravityModifier * 0.002f)) * DeltaTime);	

		moveDirection.y += FallSpeed * DeltaTime;

		// Offset correction for uneven ground
		float bumpUpOffset = 0.0f;
		
		if (Controller.isGrounded && MoveThrottle.y <= 0.001f)
		{
			bumpUpOffset = Mathf.Max(Controller.stepOffset, 
									 new Vector3(moveDirection.x, 0, moveDirection.z).magnitude); 
			moveDirection -= bumpUpOffset * Vector3.up;
		}			
	 
		Vector3 predictedXZ = Vector3.Scale((Controller.transform.localPosition + moveDirection), 
											 new Vector3(1, 0, 1));	
		
		// Move contoller
		Controller.Move(moveDirection);
		
		Vector3 actualXZ = Vector3.Scale(Controller.transform.localPosition, new Vector3(1, 0, 1));
		

		if (Network.isServer) {
			MyDebug("Server; predictedXZ = " + predictedXZ);
			MyDebug("Server; actualXZ = " + actualXZ);
		} else if (Network.isClient) {
			networkView.RPC("MyDebug", RPCMode.Server, "Client; predictedXZ = " + predictedXZ);
			networkView.RPC("MyDebug", RPCMode.Server, "Client; actualXZ = " + actualXZ);
		}



		if (predictedXZ != actualXZ)
			MoveThrottle += (actualXZ - predictedXZ) / DeltaTime; 
		
		// Update rotation using CameraController transform, possibly proving some rules for 
		// sliding the rotation for a more natural movement and body visual
		UpdatePlayerForwardDirTransform();
	}
		
	// UpdateMovement
	//
	// COnsolidate all movement code here
	//
	static float sDeltaRotationOld = 0.0f;
	public virtual void UpdateMovement()
	{
		// Do not apply input if we are showing a level selection display
		if(OVRMainMenu.sShowLevels == false)
		{
			clientMoveForward	= false;
			clientMoveLeft		= false;
			clientMoveRight		= false;
			clientMoveBack		= false;
				
			MoveScale = 1.0f;
			
			// * * * * * * * * * * *
			// Keyboard input
			
			// Move

			// WASD
			if (Input.GetKey(KeyCode.W)) clientMoveForward	= true;
			if (Input.GetKey(KeyCode.A)) clientMoveLeft		= true;
			if (Input.GetKey(KeyCode.S)) clientMoveRight	= true; 
			if (Input.GetKey(KeyCode.D)) clientMoveBack		= true; 
			// Arrow keys
			if (Input.GetKey(KeyCode.UpArrow))    clientMoveForward	= true;
			if (Input.GetKey(KeyCode.LeftArrow))  clientMoveLeft	= true;
			if (Input.GetKey(KeyCode.DownArrow))  clientMoveRight	= true; 
			if (Input.GetKey(KeyCode.RightArrow)) clientMoveBack	= true;
				
			if (Network.isServer && (clientMoveForward || clientMoveLeft || clientMoveBack || clientMoveRight || sendFalse))
			{	
				Debug.Log("Send as server");
        		SendMovementInput(clientMoveForward, clientMoveLeft, clientMoveBack, clientMoveRight);
        		if(clientMoveForward || clientMoveLeft || clientMoveBack || clientMoveRight)
        			sendFalse = true;
    		}
			else if (Network.isClient && (clientMoveForward || clientMoveLeft || clientMoveBack || clientMoveRight || sendFalse))
			{

				Debug.Log("Send as client");
        		networkView.RPC("SendMovementInput", RPCMode.Server, clientMoveForward, clientMoveLeft, clientMoveBack, clientMoveRight);
        		if(clientMoveForward || clientMoveLeft || clientMoveBack || clientMoveRight)
        			sendFalse = true;
    		}
			
			if ((currentServerMoveForward && currentServerMoveLeft) || (currentServerMoveForward && currentServerMoveRight) || (currentServerMoveBack && currentServerMoveLeft)|| (currentServerMoveBack && currentServerMoveRight))
			{
				MoveScale = 0.70710678f;
			}

			// No positional movement if we are in the air
			if (!Controller.isGrounded)	
				MoveScale = 0.0f;
			
			MoveScale *= DeltaTime;
			
			// Compute this for key movement
			float moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;
			
			// Run!
			if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
				moveInfluence *= 2.0f;
			
			if(DirXform != null)
			{
				Debug.Log("Performing movement");
				if (currentServerMoveForward)
					MoveThrottle += DirXform.TransformDirection(Vector3.forward * moveInfluence);
				if (currentServerMoveBack)
					MoveThrottle += DirXform.TransformDirection(Vector3.back * moveInfluence) * BackAndSideDampen;
				if (currentServerMoveLeft)
					MoveThrottle += DirXform.TransformDirection(Vector3.left * moveInfluence) * BackAndSideDampen;
				if (currentServerMoveRight)
					MoveThrottle += DirXform.TransformDirection(Vector3.right * moveInfluence) * BackAndSideDampen;
			} else {
				Debug.Log("DirXforce = null");
			}
			
			// Rotate
			
			// compute for key rotation
			float rotateInfluence = DeltaTime * RotationAmount * RotationScaleMultiplier;
			
			//reduce by half to avoid getting ill
			if (Input.GetKey(KeyCode.Q)) 
				YRotation -= rotateInfluence * 0.5f;  
			if (Input.GetKey(KeyCode.E)) 
				YRotation += rotateInfluence * 0.5f;
			
						// * * * * * * * * * * *
			// Mouse input
			
			// Move
			
			// Rotate
			AllowMouseRotation = true;
			float deltaRotation = 0.0f;
			if(AllowMouseRotation == false)
			{
				deltaRotation = Input.GetAxis("Mouse X") * rotateInfluence * 3.25f;
			}
			
			float filteredDeltaRotation = (sDeltaRotationOld * 0.0f) + (deltaRotation * 1.0f);
			YRotation += filteredDeltaRotation;
			sDeltaRotationOld = filteredDeltaRotation;
			
			// * * * * * * * * * * *
			// XBox controller input	
			
			// Compute this for xinput movement
			moveInfluence = Acceleration * 0.1f * MoveScale * MoveScaleMultiplier;
			
			// Run!
			moveInfluence *= 1.0f + OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftTrigger);
			
			// Move
			if(DirXform != null)
			{
				float leftAxisY = 
				OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftYAxis);
				
				float leftAxisX = 
				OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.LeftXAxis);
				
				if(leftAxisY > 0.0f)
			  			MoveThrottle += leftAxisY *
					DirXform.TransformDirection(Vector3.forward * moveInfluence);
				
				if(leftAxisY < 0.0f)
			  			MoveThrottle += Mathf.Abs(leftAxisY) *		
					DirXform.TransformDirection(Vector3.back * moveInfluence) * BackAndSideDampen;
				
				if(leftAxisX < 0.0f)
			  			MoveThrottle += Mathf.Abs(leftAxisX) *
					DirXform.TransformDirection(Vector3.left * moveInfluence) * BackAndSideDampen;
				
				if(leftAxisX > 0.0f)
					MoveThrottle += leftAxisX *
					DirXform.TransformDirection(Vector3.right * moveInfluence) * BackAndSideDampen;
			}
			
			float rightAxisX = 
			OVRGamepadController.GPC_GetAxis((int)OVRGamepadController.Axis.RightXAxis);
			
			// Rotate
			//YRotation += rightAxisX * rotateInfluence;

			// Update cameras direction and rotation
			SetCameras();
		}	
	}

	// UpdatePlayerControllerRotation
	// This function will be used to 'slide' PlayerController rotation around based on 
	// CameraController. For now, we are simply copying the CameraController rotation into 
	// PlayerController, so that the PlayerController always faces the direction of the 
	// CameraController. When we add a body, this will change a bit..
	public virtual void UpdatePlayerForwardDirTransform()
	{
		if ((DirXform != null) && (CameraController != null))
		{
			//DirXform.rotation = CameraController.transform.rotation;
			
			Transform[] Xforms = gameObject.GetComponentsInChildren<Transform>();
		
			for(int i = 0; i < Xforms.Length; i++)
			{
				if(Xforms[i].name == "ForwardDirection")
				{
					DirXform.rotation = Xforms[i].rotation;
				}
			}
		}
	}
	
	///////////////////////////////////////////////////////////
	// PUBLIC FUNCTIONS
	///////////////////////////////////////////////////////////
	
	// Jump
	public bool Jump()
	{
		if (!Controller.isGrounded)
			return false;

		MoveThrottle += new Vector3(0, JumpForce, 0);

		return true;
	}

	// Stop
	public void Stop()
	{
		Controller.Move(Vector3.zero);
		MoveThrottle = Vector3.zero;
		FallSpeed = 0.0f;
	}	
	
	// InitializeInputs
	public void InitializeInputs()
	{
		// Get our start direction
		OrientationOffset = transform.rotation;
		// Make sure to set y rotation to 0 degrees
		YRotation = 0.0f;
	}
	
	// SetCameras
	public void SetCameras()
	{
		if(CameraController != null)
		{
			// Make sure to set the initial direction of the camera 
			// to match the game player direction
			CameraController.SetOrientationOffset(OrientationOffset);
			CameraController.SetYRotation(YRotation);
		}
	}
	
	// Get/SetMoveScaleMultiplier
	public void GetMoveScaleMultiplier(ref float moveScaleMultiplier)
	{
		moveScaleMultiplier = MoveScaleMultiplier;
	}
	public void SetMoveScaleMultiplier(float moveScaleMultiplier)
	{
		MoveScaleMultiplier = moveScaleMultiplier;
	}
	
	// Get/SetRotationScaleMultiplier
	public void GetRotationScaleMultiplier(ref float rotationScaleMultiplier)
	{
		rotationScaleMultiplier = RotationScaleMultiplier;
	}
	public void SetRotationScaleMultiplier(float rotationScaleMultiplier)
	{
		RotationScaleMultiplier = rotationScaleMultiplier;
	}
	/*
   void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info) {
   		Debug.Log("JADA!");
    	if (stream.isWriting) {
			float mF = 0;
			float mB = 0;
			
			if(currentServerMoveForward) mF = 1;
			if(currentServerMoveBack) mB = 1;

        	Vector3 movement = new Vector3(mF,mB,0);
        	stream.Serialize(ref movement);
    	} else {
        	Vector3 movementReceived = Vector3.zero;
        	stream.Serialize(ref movementReceived);
			if(movementReceived[0] == 1) {
				currentServerMoveForward = true;
			} else {
				currentServerMoveForward = false;
			}
			
			if(movementReceived[1] == 1){
				currentServerMoveBack = true;
			} else {
				currentServerMoveBack = false;
			}
    	}
	}
	*/
}

