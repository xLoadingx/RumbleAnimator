using System.Reflection;
using MelonLoader;
using UnityEngine;
using Il2CppRUMBLE.MoveSystem;
using Il2CppRUMBLE.Players;
using Il2CppRUMBLE.Players.Subsystems;
using Il2CppRUMBLE.Pools;
using RumbleModdingAPI;
using RumbleModUI;
using Input = UnityEngine.Input;
using Stack = Il2CppRUMBLE.MoveSystem.Stack;
using RumbleAnimator.Recording;
using RumbleAnimator.Utils;
using UnityEngine.Events;

[assembly: MelonInfo(typeof(RumbleAnimator.Main), RumbleAnimator.BuildInfo.Name, RumbleAnimator.BuildInfo.Version, RumbleAnimator.BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]

namespace RumbleAnimator
{
	// If you're reading this, welcome to binary hek.
	// Good luck, have fun. I sure as heck didn't - ERROR
	
	// Thanks to Blank and SaveForth for their binary wizardry and
	// clonebending techniques, respectively.
	
    public static class BuildInfo
    {
        public const string Name = "RumbleAnimator";
        public const string Author = "ERROR";
        public const string Version = "1.0.0";
    }
    
    public class Main : MelonMod
    {
		public static string currentScene = "Loader";

		public static bool isRecording = false;
		public static bool isPlaying = false;

		private static Debouncer recordDebounce = new();
		private static Debouncer playbackDebounce = new();

		public static Dictionary<string, PlayerReplayState> players = new();
		
		public static List<GameObject> structures = new();
		public static List<List<StructureFrame>> structureDataList = new();
		public Dictionary<GameObject, UnityAction> destructionHooks = new();
		
		private HashSet<string> recordedVisuals = new();

		public static List<byte> _writeBuffer = new();

		public static float recordingStartTime;
		public static float playbackStartTime;
		
		public static int currentRecordingFrame = 0;
		
		private GameObject bracelet;

		private Mod rumbleAnimatorMod = new();
		private ModSetting<float> SlabDistance = new();
		private ModSetting<float> HeightOffset = new();
		private ModSetting<bool> BraceletHand = new();

		public static Assembly ModAssembly;

		public override void OnLateInitializeMelon()
		{
			HarmonyInstance.PatchAll();
			UI.instance.UI_Initialized += OnUIInit;
			Calls.onMapInitialized += Initialize;
			
			ModAssembly = MelonAssembly.Assembly;
		}

		public void Initialize()
		{
			if (!SlabBuilder.IsBuilt)
				SlabBuilder.BuildSlab();
			
			if (currentScene is not "Loader")
				InitializeRecordingBracelet();
			
			isRecording = false;
			isPlaying = false;

			players?.Clear();
		}

		public override void OnSceneWasLoaded(int buildIndex, string sceneName)
		{
			currentScene = sceneName;

			if (isRecording)
			{
				try
				{
					ReplayFile.ReplayWriter?.Write(_writeBuffer.ToArray());
					ReplayFile.ReplayWriter?.Flush();
					ReplayFile.ReplayWriter?.Close();
				}
				catch (Exception e)
				{
					MelonLogger.Error($"[RumbleAnimator] Failed to write replay on scene load: {e}");
				}
				finally
				{
					ReplayFile.ReplayWriter = null;
					_writeBuffer.Clear();
					isRecording = false;
				}
			}
		}

		public override void OnUpdate()
		{
			if (currentScene is "Loader")
				return;

			bool joystickR = Calls.ControllerMap.RightController.GetJoystickClick() is 1f;
			bool rKey = Input.GetKey(KeyCode.R);
			
			if (recordDebounce.JustPressed(rKey || joystickR))
			{
				if (!isRecording)
				{
					MelonLogger.Msg("[RumbleAnimator] Recording");
					players.Clear();
					recordingStartTime = Time.time;

					ReplayFile.InitializeReplayFile();
				}
				else
				{
					ReplayFile.ReplayWriter?.Write(_writeBuffer.ToArray());
					_writeBuffer.Clear();

					ReplayFile.ReplayWriter?.Close();
					ReplayFile.ReplayWriter = null;
					
					MelonLogger.Msg("[RumbleAnimator] Recording stopped and file saved.");
				}
				
				// indicator?.SetActive(!indicator.activeSelf);
				isRecording = !isRecording;
			}

			if (currentScene is "Map0" or "Map1") 
				return;

			bool joystickL = Calls.ControllerMap.LeftController.GetJoystickClick() is 1f;
			bool lKey = Input.GetKey(KeyCode.L);

			if (playbackDebounce.JustPressed(lKey || joystickL) && !isRecording)
			{
				// ReplayFile.PromptReplayFile(path =>
				// {
				// 	var (loadedPlayerData, loadedStructureFrames) = ReplayFile.GetReplayFromFile(path);
				// 	if (loadedPlayerData != null)
				// 	{
				// 		players = loadedPlayerData;
				// 		structureDataList = loadedStructureFrames;
				// 		
				// 		MelonLogger.Msg("[RumbleAnimator] Playing");
				// 		playbackStartTime = Time.time;
				// 	
				// 		isPlaying = true;
				// 	}
				// });
				
				SlabBuilder.ShowSlab();
			}
		}

		public override void OnFixedUpdate()
		{
			void SetPositionsAndRotations(FramePoseData pos, FrameRotationData rot, CloneBuilder.CloneInfo clonePlayer)
			{
				clonePlayer.LeftHand.transform.localPosition = pos.lHandPos.ToVector3();
				clonePlayer.LeftHand.transform.localRotation = rot.lHandRot.ToQuaternion();
				clonePlayer.DLeftHand.transform.localPosition = pos.rHandPos.ToVector3();
				clonePlayer.DLeftHand.transform.localRotation = rot.rHandRot.ToQuaternion();
				
				clonePlayer.RightHand.transform.localPosition = pos.rHandPos.ToVector3();
				clonePlayer.RightHand.transform.localRotation = rot.rHandRot.ToQuaternion();
				clonePlayer.DRightHand.transform.localPosition = pos.lHandPos.ToVector3();
				clonePlayer.DRightHand.transform.localRotation = rot.lHandRot.ToQuaternion();
				
				clonePlayer.Head.transform.localPosition = pos.headPos.ToVector3();
				clonePlayer.Head.transform.localRotation = rot.headRot.ToQuaternion();
				clonePlayer.DHead.transform.localPosition = pos.headPos.ToVector3();
				clonePlayer.DHead.transform.localRotation = rot.headRot.ToQuaternion();
				
				clonePlayer.VRRig.transform.position = pos.vrPos.ToVector3();
				clonePlayer.VRRig.transform.rotation = rot.vrRot.ToQuaternion();
				clonePlayer.dVRRig.transform.position = pos.vrPos.ToVector3();
				clonePlayer.dVRRig.transform.rotation = rot.vrRot.ToQuaternion();
				
				clonePlayer.RootObject.transform.position = pos.controllerPos.ToVector3();
				clonePlayer.RootObject.transform.rotation = rot.controllerRot.ToQuaternion();
				clonePlayer.BodyDouble.transform.position = pos.controllerPos.ToVector3();
				clonePlayer.BodyDouble.transform.rotation = rot.controllerRot.ToQuaternion();
				
				clonePlayer.BodyDouble.transform.position = new Vector3(
					pos.controllerPos.x,
					pos.visualsY,
					pos.controllerPos.z
				);
			}
			
			if (isRecording)
			{
				// Player recording
				foreach (var player in Calls.Players.GetAllPlayers())
				{
					var masterID = player.Data.GeneralData.PlayFabMasterId;
					
					if (!players.TryGetValue(masterID, out var state))
					{
						state = new PlayerReplayState(masterID, player.Data.VisualData.ToPlayfabDataString());
						players[masterID] = state;
					}

					if (recordedVisuals.Add(masterID))
					{
						using var ms = new MemoryStream();
						using var bw = new BinaryWriter(ms);
						bw.Write(masterID);
						bw.Write(state.Data.VisualData);
						ReplayFile.WriteFramedData(ms.ToArray(), currentRecordingFrame, FrameType.VisualData);
					}
					
					Transform playerController = player.Controller.transform;
					Transform VR = playerController.GetChild(1);
					Transform Visuals = playerController.GetChild(0);
					Transform rController = VR.transform.GetChild(2);
					Transform lController = VR.transform.GetChild(1);
					Transform Headset = VR.transform.GetChild(0).GetChild(0);
				
					var poseData = new FramePoseData
					{
						lHandPos = lController.localPosition.ToSerializable(),
						rHandPos = rController.localPosition.ToSerializable(),
						headPos = Headset.localPosition.ToSerializable(),
						visualsY = Visuals.position.y,
						vrPos = VR.position.ToSerializable(),
						controllerPos = playerController.position.ToSerializable(),
					};
				
					var rotationData = new FrameRotationData
					{
						lHandRot = lController.localRotation.ToSerializable(),
						rHandRot = rController.localRotation.ToSerializable(),
						headRot = Headset.localRotation.ToSerializable(),
						vrRot = VR.transform.rotation.ToSerializable(),
						controllerRot = playerController.rotation.ToSerializable()
					};
				
					var frameData = new FrameData
					{
						positions = poseData,
						rotations = rotationData
					};
					
					var timestamp = Time.time - recordingStartTime;
					var data = Codec.EncodePlayerFrame(masterID, frameData, timestamp);
					ReplayFile.WriteFramedData(data, currentRecordingFrame, FrameType.PlayerUpdate);
				}
				
				// Structure recording
				for (var i = 0; i < structures.Count; i++)
				{
					var structure = structures[i];
					var timestamp = Time.time - recordingStartTime;

					if (structure.GetComponent<Structure>().isSpawning)
						continue;
					
					while (structureDataList.Count <= i)
						structureDataList.Add(new List<StructureFrame>());

					var frames = structureDataList[i];

					// if (!destructionHooks.ContainsKey(structure))
					// {
					// 	UnityAction hook = null;
					// 	hook = (UnityAction)(() =>
					// 	{
					// 		if (Main.isRecording && replayData.destroyedAtFrame == null)
					// 			replayData.destroyedAtFrame = currentRecordingFrame;
					// 		
					// 		structure.GetComponent<Structure>().onStructureDestroyed.RemoveListener(hook);
					// 		destructionHooks.Remove(structure);
					// 	});
					//
					// 	destructionHooks[structure] = hook;
					// 	
					// 	structure.GetComponent<Structure>().onStructureDestroyed.AddListener(hook);
					// }
					
					if (frames.Count > 0)
					{
						var prevFrame = frames[^1];
						if (!Utilities.HasTransformChanged(
							    structure.transform.position, structure.transform.rotation,
							    prevFrame.position.ToVector3(), prevFrame.rotation.ToQuaternion())) 
							continue;
					}

					var newFrame = new StructureFrame
					{
						timestamp = timestamp,
						position = new SVector3(structure.transform.position),
						rotation = new SQuaternion(structure.transform.rotation)
					};
					
					frames.Add(newFrame);

					var data = Codec.EncodeStructureFrame(newFrame, i, timestamp);
					ReplayFile.WriteFramedData(data, currentRecordingFrame, FrameType.StructureUpdate);
				}

				if (_writeBuffer.Count >= 1024)
				{
					ReplayFile.ReplayWriter.Write(_writeBuffer.ToArray());
					_writeBuffer.Clear();
				}
			}

			if (isPlaying)
			{
				float time = Time.time - playbackStartTime;

				foreach (var player in players)
				{
					string masterID = player.Value.Data.MasterID;

					if (!players.TryGetValue(masterID, out var state))
					{
						MelonLogger.Warning($"[Replay] Tried to access missing player state for {masterID}");
						return;
					}

					state.Clone ??= CloneBuilder.BuildClone(state.Data.Frames[0].positions.controllerPos.ToVector3(), state.Data.VisualData, masterID);

					var currentClone = state.Clone;
					int currentPlayerFrame = state.CurrentPlayerFrameIndex;
					int currentStackFrame = state.CurrentStackFrameIndex;

					if (currentPlayerFrame >= state.Data.Frames.Count)
					{
						StopPlaybackFor(masterID);

						if (players.Count is 0)
						{
							isPlaying = false;

							foreach (var structure in structures)
								StopPlaybackForStructure(structure);
							
							structures.Clear();
							structureDataList.Clear();
							var playerController = Calls.Players.GetLocalPlayer().Controller.transform;
							playerController.GetChild(0).GetComponent<PlayerVisuals>().Initialize(playerController.GetComponent<PlayerController>());
						}
							

						continue;
					}
					
					var playerFrame = state.Data.Frames[currentPlayerFrame];

					if (time >= playerFrame.timestamp)
					{
						SetPositionsAndRotations(playerFrame.positions, playerFrame.rotations, state.Clone);
						
						state.CurrentPlayerFrameIndex++;
					}

					if (state.CurrentStackFrameIndex < state.Data.StackEvents.Count)
					{
						var stackFrame = state.Data.StackEvents[currentStackFrame];

						if (time >= stackFrame.timestamp)
						{
							Stack stack = null;

							foreach (var availableStack in Calls.Players.GetLocalPlayer().Controller.GetComponent<PlayerStackProcessor>().availableStacks)
							{
								if (availableStack.CachedName == stackFrame.stack)
								{
									stack = availableStack;
									break;
								}
							}

							if (stack != null)
								currentClone.StackProcessor.Execute(stack, null);
							
							state.CurrentStackFrameIndex++;
						}
					}
				}

				for (int i = 0; i < structures.Count; i++)
				{
					var structure = structures[i];
					var frames = structureDataList[i];
				
					if (frames.Count is 0)
						continue;

					// while (frames.CurrentFrameIndex + 1 < frames.Count &&
					//        frames[replayData.CurrentFrameIndex + 1].timestamp <= time)
					// {
					// 	replayData.CurrentFrameIndex++;
					// }

					// if (replayData.destroyedAtFrame == replayData.CurrentFrameIndex && !structure.GetComponent<PooledMonoBehaviour>().isInPool)
					// 	StopPlaybackForStructure(structure);
					
					MelonLogger.Msg($"Frame Count: {frames.Count}");
					StructureFrame? frame = frames.LastOrDefault(f => f.timestamp <= time);
				
					if (frame.HasValue)
					{
						var tr = structure?.transform;
						if (tr is null)
							continue;
						
						structure.transform.SetPositionAndRotation(frame.Value.position.ToVector3(), frame.Value.rotation.ToQuaternion());
						MelonLogger.Msg($"[Replay] Setting structure {structure.name} | Position = {frame.Value.position.ToVector3()} | Rotation = {frame.Value.rotation.ToQuaternion()}");
						
						
					}
				}
			}
		}

		private void StopPlaybackFor(string masterID)
		{
			var state = players[masterID];
			GameObject.Destroy(state.Clone.RootObject);
			GameObject.Destroy(state.Clone.BodyDouble);
			state.Clone = null;

			players.Remove(masterID);
			MelonLogger.Msg($"Replay ended for player {masterID}");
		}

		private void StopPlaybackForStructure(GameObject structure)
		{
			structure.GetComponent<PooledMonoBehaviour>().ReturnToPool();
			structure.GetComponent<Rigidbody>().isKinematic = false;

			foreach (var collider in structure.GetComponentsInChildren<Collider>())
				collider.enabled = true;
		}

		private void InitializeRecordingBracelet()
		{
			// if (bracelet == null)
			// {
			// 	var bundle = Utilities.LoadBundle("bracelet");
			// 	bracelet = bundle.LoadAsset<GameObject>("Bracelet");
			// 	bundle.Unload(false);
			// 	
			// 	var visuals = bracelet.AddComponent<Components.BraceletVisuals>();
			// 	visuals.StartPulse();
			// }
			//
			// int hand = (bool)BraceletHand.SavedValue ? 2 : 3;
			//
			// bracelet.transform.SetParent(Calls.Players.GetLocalPlayer().Controller.transform.GetChild(4).GetChild(hand));
			// bracelet.transform.localPosition = Vector3.zero;
		}

		public void OnUIInit()
		{
			rumbleAnimatorMod.ModName = "RumbleAnimator";
			rumbleAnimatorMod.ModVersion = "1.0.0";
			rumbleAnimatorMod.SetFolder("MatchReplays");

			SlabDistance = rumbleAnimatorMod.AddToList(
				"Settings Distance",
				0.5f,
				"The distance of the replay settings slab appears from you.",
				new Tags()
			);
			
			HeightOffset = rumbleAnimatorMod.AddToList(
				"Settings Height Offset",
				-0.25f,
				"The difference in height the replay settings slab is from your headset.",
				new Tags()
			);
			
			BraceletHand = rumbleAnimatorMod.AddToList(
				"Recording Bracelet Hand", 
				false, 
				0, 
				"Changes which hand the bracelet appears on when recording.\nFalse is the right hand, true is the left hand.", 
				new Tags()
			);
			
			rumbleAnimatorMod.GetFromFile();
			rumbleAnimatorMod.ModSaved += ModSaved;
			ModSaved();
			
			UI.instance.AddMod(rumbleAnimatorMod);
		}

		private void ModSaved()
		{
			SlabBuilder.Distance = (float)SlabDistance.SavedValue;
			SlabBuilder.HeightOffset = (float)HeightOffset.SavedValue;
			
			if (currentScene is not "Loader")
				InitializeRecordingBracelet();
			
			if (SlabBuilder.slabPrefab.transform.GetChild(0).GetChild(0).GetChild(0).gameObject.activeSelf)
				SlabBuilder.ShowSlab();
		}
    }

    public class StructureReplayData
    {
	    public List<StructureFrame> frames;
	    public int? destroyedAtFrame = null;
	    public int CurrentFrameIndex = 0;
    }
    
    public class PlayerReplayState
    {
	    public PlayerReplayData Data;
	    public CloneBuilder.CloneInfo Clone;
	    public int CurrentPlayerFrameIndex = 0;
	    public int CurrentStackFrameIndex = 0;

	    public PlayerReplayState(string masterID, string visualData)
	    {
		    Data = new PlayerReplayData(masterID, visualData);
	    }
    }

    [Serializable]
    public class PlayerReplayData
    {
	    public string MasterID;
	    public List<FrameData> Frames = new();
	    public List<StackEvent> StackEvents = new();
	    public string VisualData;

	    public PlayerReplayData(string MasterID, string VisualData)
	    {
		    this.MasterID = MasterID;
			this.VisualData = VisualData;
	    }
    }
    
    [RegisterTypeInIl2Cpp]
    public class ReplayClone : MonoBehaviour { }
}