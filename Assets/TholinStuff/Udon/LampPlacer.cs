using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LampPlacer : UdonSharpBehaviour {
	public GameObject ceilingRoot;
	public int numberOfLights = 100;
	[InspectorName("Set this to 16 times the ceiling texture scale!")] public int tileCount = 11 * 16;
	public GameObject lampTemplate;
	public Material lampMaterial;
	public Light lampLight;
	public Color lightColor = Color.white;
	public Xorshift rng;

    //Note that this is only run once for the entire world. Every chunk has the same layout of lights, but this is acceptable, as its not noticable whatsoever from inside the backrooms.
	public void _PlaceCeilingDecorations() {
		if(lampMaterial != null) lampMaterial.SetColor("_Color", lightColor);
		if(lampLight != null) lampLight.color = lightColor;
		float scale = 1.0f / (float)tileCount;
		lampTemplate.transform.localScale = new Vector3(scale, scale, scale);
		int range = tileCount - 2;
        
        //Randomly generate light positions. Don't care about collissions just yet.
		int[] lampPositionsX = new int[numberOfLights];
		int[] lampPositionsY = new int[numberOfLights];
		bool[] shouldPlace = new bool[numberOfLights];
		rng.SetState(32162365);
		for(int i = 0; i < numberOfLights; i++) {
			lampPositionsX[i] = (int)(rng.NextFloat() * range * 0.1f) * 10;
			lampPositionsY[i] = (int)(rng.NextFloat() * range * 0.1f) * 10;
		}
		
		/*
         * Check for colissions of generated coordinates and mark duplicates to not have a lamp placed for them.
         * This does mean that there may be slightly less then numberOfLights being generated, but it shouldn't be noticable.
         */
		for(int i = 0; i < numberOfLights; i++) shouldPlace[i] = true;
		for(int i = 0; i < numberOfLights - 1; i++) {
			if(!shouldPlace[i]) continue;
			int tilex = lampPositionsX[i];
			int tiley = lampPositionsY[i];
			for(int j = i + 1; j < numberOfLights; j++) {
				if(lampPositionsX[j] == tilex && lampPositionsY[j] == tiley) {
					shouldPlace[j] = false;
				}
			}
		}
		
		//Actually place the lights if the above code deemed it safe to do so
		for(int i = 0; i < numberOfLights; i++) {
			if(shouldPlace[i]) {
				int tilex = lampPositionsX[i];
				int tiley = lampPositionsY[i];
				GameObject newLamp = Object.Instantiate(lampTemplate, ceilingRoot.transform);
				newLamp.transform.localPosition = new Vector3(tilex * scale * 2 - 1, tiley * scale * 2 - 1, 0.04f * scale);
			}
		}
		
		//Lastly, de-activate the template
		lampTemplate.SetActive(false);
	}
}
