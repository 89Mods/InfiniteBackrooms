using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * This class doesn't actually contain any world generation code, but acts as a sort of "World generation director" that handles chunk loading, and helps the chunks trigger generation when they need to.
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class WorldGenerator : UdonSharpBehaviour {
	//Don't change pls
	private const int WORLD_SIZE = 6;

	[Header("Chunks setup")]
	public GameObject chunkRoot;
	public int chunkSize = 8;
	public GameObject chunkTemplate;
	public PlayerChunkPosition playerPos;
	public LampPlacer ceilingDecorator;
	
	[Header("World gen setup")]
	[InspectorName("Max chunks to be generated in parallel")] public int maxParallelChunks = 1;
	[InspectorName("Max maze generation iterations per frame")] public int generationIters = 8;
	[InspectorName("Max chunk build iterations per frame")] public int buildIters = 8;
	public int worldSeed = 523416426;
	[InspectorName("Randomize seed on world load (overrides fixed seed above)?")] public bool randomWorldSeed = false;

	[UdonSynced] public int syncedSeed = 0;
	
	/*
	* The array of all chunks. Once again a flat array because Udon dies if I try to use a 2D array.
	*/
	[HideInInspector] public Chunk[] chunks = new Chunk[WORLD_SIZE * WORLD_SIZE];
	[HideInInspector] public int chunkPntr = 0;
	[HideInInspector] public int chunksGenerating = 0;
	[HideInInspector] public bool chunksInstantiated = false;
	[HideInInspector] public int initIters;
	[HideInInspector] public bool ready = false;
    
	private bool seedIsSynced = false;

	void Start() {
        playerPos.worldGen = this;
		initIters = buildIters * 4;
        //Decorate ceiling, then instantiate all chunks, re-using the chunkTemplate as Element #0.
		if(ceilingDecorator != null) ceilingDecorator._PlaceCeilingDecorations();
		for(int i = 0; i < (WORLD_SIZE * WORLD_SIZE) - 1; i++) {
			Object.Instantiate(chunkTemplate, chunkRoot.transform);
		}
		chunksInstantiated = true;
		//Reset this, just in case
		chunkRoot.transform.localScale = new Vector3(1, 1, 1);
		chunkRoot.transform.position = new Vector3(0, 0, 0);
		chunkRoot.transform.rotation = Quaternion.Euler(0, 0, 0);
        
		//Synchronize the world seed so that everyone's generating the same world. Instance master is the one to distribute the seed to late-joiners.
		if(Networking.LocalPlayer != null && Networking.LocalPlayer.isMaster) {
			if(randomWorldSeed) {
				worldSeed = (int)(Random.value * 2000000000);
			}
			seedIsSynced = true;
			if(!Networking.IsOwner(this.gameObject)) Networking.SetOwner(Networking.LocalPlayer, this.gameObject);
			syncedSeed = worldSeed;
			RequestSerialization();
		}
		ready = false;
	}

	/*
	* Initializes the chunk positions after all chunks have initialized and the world seed is synced.
	*/
	void Update() {
		if(!ready) {
			CheckReady();
			if(ready) {
				Debug.Log("World generator ready!");
				SetInitialChunkPositions();
			}
			return;
		}

		UpdateChunkPositions();
	}

	//Receive synced seed
	public override void OnDeserialization() {
		seedIsSynced = true;
		worldSeed = syncedSeed;
	}

	//Distribute all chunks in a square surrounding the player
	private void SetInitialChunkPositions() {
		int px = (playerPos.x / 2) * 2;
		int py = (playerPos.y / 2) * 2;
		for(int i = 0; i < WORLD_SIZE; i++) {
			for(int j = 0; j < WORLD_SIZE; j++) {
				Chunk c = chunks[i * WORLD_SIZE + j];
				c.posX = i + px - (WORLD_SIZE / 2);
				c.posY = j + py - (WORLD_SIZE / 2);
			}
		}
		testX = px;
		testY = py;
	}

	/*
	* Moves two columns of chunks from one end of the square of chunks to the other.
	* This cleverly avoids having to delete and re-instantiate chunk objects as the player moves by always only moving around existing ones.
	*/
	private void MoveChunksX(bool direction, int playerY) {
		playerY /= 2;
		playerY *= 2;
		int newX,cutoff;
		int cntr = 0;
		if(direction) {
			newX = playerPos.x + (WORLD_SIZE / 2) - 2;
			cutoff = playerPos.x - (WORLD_SIZE / 2);
		}else {
			newX = playerPos.x - (WORLD_SIZE / 2);
			cutoff = playerPos.x + (WORLD_SIZE / 2) - 1;
		}
		int initialY = playerY - (WORLD_SIZE / 2);
		int newY = initialY;
		for(int i = 0; i < WORLD_SIZE * WORLD_SIZE; i++) {
			Chunk c = chunks[i];
			if((direction && c.posX < cutoff) || (!direction && c.posX > cutoff)) {
				c.posX = newX;
				c.posY = newY;
				newY++;
				if(newY == initialY + WORLD_SIZE) {
					newY = initialY;
					newX++;
				}
				cntr++;
				if(cntr == WORLD_SIZE * 2) break;
			}
		}
	}

	//Same as above, but moves two rows of chunks along the Y-axis.
	private void MoveChunksY(bool direction, int playerX) {
		playerX /= 2;
		playerX *= 2;
		int newY,cutoff;
		int cntr = 0;
		if(direction) {
			newY = playerPos.y + (WORLD_SIZE / 2) - 2;
			cutoff = playerPos.y - (WORLD_SIZE / 2);
		}else {
			newY = playerPos.y - (WORLD_SIZE / 2);
			cutoff = playerPos.y + (WORLD_SIZE / 2) - 1;
		}
		int initialX = playerX - (WORLD_SIZE / 2);
		int newX = initialX;
		for(int i = 0; i < WORLD_SIZE * WORLD_SIZE; i++) {
			Chunk c = chunks[i];
			if((direction && c.posY < cutoff) || (!direction && c.posY > cutoff)) {
				c.posX = newX;
				c.posY = newY;
				newX++;
				if(newX == initialX + WORLD_SIZE) {
					newX = initialX;
					newY++;
				}
				cntr++;
				if(cntr == WORLD_SIZE * 2) break;
			}
		}
	}
	
	private int testX,testY;
	
	//Debug functions to check if the chunks still form a perfect square (everything breaks if they do not).
	private bool CheckChunks() {
		int maxX = -100000;
		int maxY = -100000;
		for(int i = 0; i < WORLD_SIZE * WORLD_SIZE; i++) {
			Chunk c = chunks[i];
			if(c.posX > maxX) maxX = c.posX;
			if(c.posY > maxY) maxY = c.posY;
		}
		for(int i = 0; i < WORLD_SIZE; i++) {
			for(int j = 0; j < WORLD_SIZE; j++) {
				bool found = false;
				for(int k = 0; k < WORLD_SIZE * WORLD_SIZE; k++) {
					Chunk c = chunks[k];
					if(c.posX == maxX - i && c.posY == maxY - j) {
						found = true;
						break;
					}
				}
				if(!found) return false;
			}
		}
		return true;
	}
	
	/*
	* Moves the chunks as the player moves, to give the apperance of an infinite world.
	* Algorithm works like a treadmil, where chunks farthest aways to the player are moved to be in front of them, so they can keep walking forever.
	* On each axis, chunk moves only trigger in the positive direction when the player crosses a chunk border at an even coordinate,
	* and only trigger in the negative direction at an uneven coordinate.
	* This prevents a scenario where the player repeatedly crosses back and forth over the same chunk border, causing chunk loading and world generation each time.
	*/
	private void UpdateChunkPositions() {
		if(playerPos.x == playerPos.prevX && playerPos.y == playerPos.prevY) return;
		if(Mathf.Abs(playerPos.x - playerPos.prevX) > 1 || Mathf.Abs(playerPos.y - playerPos.prevY) > 1) { //Player teleported/respawned, so reset the entire thing
			SetInitialChunkPositions();
			return;
		}
		bool movePosX = playerPos.x > playerPos.prevX && playerPos.x % 2 == 0;
		bool moveNegX = playerPos.x < playerPos.prevX && playerPos.x % 2 == 0;
		bool movePosY = playerPos.y > playerPos.prevY && playerPos.y % 2 == 0;
		bool moveNegY = playerPos.y < playerPos.prevY && playerPos.y % 2 == 0;
		if(movePosX) {
			MoveChunksX(true, testY);
			testX = playerPos.x;
		}else if(moveNegX) {
			MoveChunksX(false, testY);
			testX = playerPos.x;
		}
		if(movePosY) {
			MoveChunksY(true, testX);
			testY = playerPos.y;
		}else if(moveNegY) {
			MoveChunksY(false, testX);
			testY = playerPos.y;
		}
		/*if(movePosX || moveNegX || movePosY || moveNegY) {
			if(!CheckChunks()) {
				Debug.Log("CHUNKS ARE BROKEN!");
				Debug.Log("On movement from (" + playerPos.prevX + "," + playerPos.prevY + ") to (" + playerPos.x + "," + playerPos.y + ")");
			}
		}*/
	}

	/*
     * Sets "ready" to true if the world seed has been synced and all chunks initialized. Sets it to false otherwise.
     */
	private void CheckReady() {
		ready = false;
		if(!seedIsSynced) return;
		ready = true;
		for(int i = 0; i < WORLD_SIZE * WORLD_SIZE; i++) {
			if(chunks[i] == null) {
				ready = false;
				return;
			}
		}
	}
}
