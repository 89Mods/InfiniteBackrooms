using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * This keeps track of the chunk the player is currently in. This information is used by the other scripts in this world.
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class PlayerChunkPosition : UdonSharpBehaviour {
	[HideInInspector] public int x = 0,y = 0,prevX = 0,prevY = 0;
	[HideInInspector] public WorldGenerator worldGen;

	void Update() {
		VRCPlayerApi player = Networking.LocalPlayer;
		if(player == null || worldGen == null) { //There's a few frames before the player loads (when joining) / after the player unloads (when leaving) when this may be null.
			return;
		}
		Vector3 playerPos = player.GetPosition();
		playerPos /= worldGen.chunkSize;
		prevX = x;
		prevY = y;
		x = (int)(playerPos.x + 0.5f);
		y = (int)(playerPos.z + 0.5f);
	}
}
