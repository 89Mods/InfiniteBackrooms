using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * Things break really hard if this world's scripts run while there is no local player present, which is the case during world upload.
 * The Unity editor essentially crashes in this case.
 * To prevent this, disable all GOs with UdonBehaviours on them when there is no player.
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DisableOnUpload : UdonSharpBehaviour {
	public GameObject[] toDisable;

	void Start() {
		if(Networking.LocalPlayer == null) {
			for(int i = 0; i < toDisable.Length; i++) toDisable[i].SetActive(false);
		}else this.gameObject.SetActive(false);
	}
}
