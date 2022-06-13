using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * Handles world generation for a single chunk. Triggers if the player gets close to an un-generated chunk.
 * Will run the maze generator, then turn the output of that into a series of meshes.
 * The mesh is split up in order for lighting not to break.
 * If it was one huge mesh, it would be affected by too many light sources at once and lighting would break.
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Chunk : UdonSharpBehaviour {
    //Determines how many meshes the chunk will be made out of. The chunk will consist of a grid of (2^meshFractPow) by (2^meshFractPow) meshes. DO NOT CHANGE PLEASE!
	public const int meshFractPow = 2;
	
	public WorldGenerator worldGen;
	public PlayerChunkPosition playerPos;
	public float wallHeight = 0.25f;
	public float wallThicknessMultiplier = 1;
	public GameObject ceiling; //Set this to the ceiling. MUST be a quad and parented to the mazeRendererTemplate GO!
	public GameObject floor; //Set this to the floor. MUST be a quad and parented to the mazeRendererTemplate GO!
	/*
	 * Optimally, a GO to which mazeRendererTemplate and ceiling are parented to.
	 * Will be disabled for unloaded chunks for optimization.
	 */
	public GameObject renderersRoot;
	public Texture2D debugTex; //If set, will render this chunk's maze to a texture.
	/*
	 * GO which MUST have MeshFilter, MeshRenderer and MeshCollider components
	 * A quad to act as the floor and one to act as the ceiling should also be parented to it and set as the "floor" and "ceiling" parameters above respectively.
	 */
	public GameObject mazeRendererTemplate;
	
	
	[HideInInspector] public int posX,posY = 10000; //This chunk's position
	
	private int lastPosX,lastPosY;
	private MazeGenerator mazeGen;
	private bool needsGeneration = false;
	private int currInitIter,currBuildIter;
	private int currStep = 0;
	private MeshCollider mCollider;
	private MeshFilter[] componentMeshFilters;
	private Mesh[] componentMeshes;
	private MeshCollider[] componentMeshColliders;
	private int meshCount;
	private int widthInMeshes;
	private int currMesh;
	private int meshSize;
	private bool isInitialized = false;
	
	/*
	 * Initialization function. Not using regular Start here to ensure that this is only run after the WorldGenerator has initialized some parameters.
	 */
	void LateStart() {
		this.name = "Chunk #" + worldGen.chunkPntr;
		worldGen.chunks[worldGen.chunkPntr] = this;
		worldGen.chunkPntr++;
		//Reset this, just in case
		transform.position = new Vector3(0, 0, 0);
		transform.rotation = Quaternion.Euler(-90, 0, 0);

		posX = posY = 0;
		lastPosX = lastPosY = 100000;
		mazeGen = GetComponent<MazeGenerator>();

		//Create arrays
		widthInMeshes = 1;
		for(int i = 0; i < meshFractPow; i++) widthInMeshes *= 2;
		meshCount = widthInMeshes * widthInMeshes;
		componentMeshFilters = new MeshFilter[meshCount];
		componentMeshes = new Mesh[meshCount];
		componentMeshColliders = new MeshCollider[meshCount];
		meshSize = mazeGen.mazeSize / widthInMeshes;

		//Set the correct scale and position for the floor and ceiling quads.
		float scale = 1.0f / widthInMeshes;
		floor.transform.localScale = new Vector3(scale, scale, scale);
		floor.transform.localPosition = new Vector3(-1 + scale, -1 + scale, 0.01f / (worldGen.chunkSize / 2.0f));
		float wallHeightFixed = wallHeight / (worldGen.chunkSize / 2.0f);
		wallHeightFixed *= 0.9f;
		ceiling.transform.localPosition = new Vector3(0, 0, wallHeightFixed);

		//Duplicate the renderer GO as many times as needed. Recycle the template as element #0 in this array of mesh renderers.
		componentMeshFilters[0] = mazeRendererTemplate.GetComponent<MeshFilter>();
		componentMeshColliders[0] = mazeRendererTemplate.GetComponent<MeshCollider>();
		componentMeshes[0] = new Mesh();
		componentMeshFilters[0].mesh = componentMeshes[0];
		mazeRendererTemplate.transform.localPosition = new Vector3(0, 0, 0);
		for(int i = 1; i < meshCount; i++) {
			GameObject newMr = Object.Instantiate(mazeRendererTemplate, renderersRoot.transform);
			newMr.transform.localPosition = new Vector3((i % widthInMeshes) / ((float)widthInMeshes * 0.5f), (i / widthInMeshes) / ((float)widthInMeshes * 0.5f), 0);
			newMr.transform.localRotation = Quaternion.identity;
			componentMeshFilters[i] = newMr.GetComponent<MeshFilter>();
			componentMeshColliders[i] = newMr.GetComponent<MeshCollider>();
			componentMeshes[i] = new Mesh();
			componentMeshFilters[i].mesh = componentMeshes[i];
		}
		isInitialized = true;
	}

	void Update() {
		if(!worldGen.chunksInstantiated) return;
		if(!isInitialized) LateStart();
		if(IsPlayerAdjacent()) {
			renderersRoot.SetActive(true);
		}
		else {
			renderersRoot.SetActive(false);
		}
		if(!worldGen.ready) return;
		if(lastPosX != posX || lastPosY != posY) OnPositionChanged(); //Fire handler for if the chunk moved. See function description.
		lastPosX = posX;
		lastPosY = posY;
		/*
		 * Trigger world generation if
		 *  - The chunk hasn't been generated yet and generation isn't already in progress
		 *  - The number of max parallel chunk generations hasn't been exceeded
		 *  - The player is close enough to see the chunk
		 */
		if(needsGeneration && worldGen.chunksGenerating < worldGen.maxParallelChunks && IsPlayerAdjacent() && currStep == 0) {
			needsGeneration = false;
			currStep = 1;
			currInitIter = 0;
			currBuildIter = 0;
			mazeGen._SetSeed(posX * 13263126 + posY * 2154135 + worldGen.worldSeed);
			mazeGen._Init(posX == 0 && posY == 0); //Reset the maze generator and set it to place the spawn room if this chunk is at the coordinate origin (0, 0).
			worldGen.chunksGenerating++;
		}
		if(currStep == 0) return; //Nothing to be done
		if(currStep == 1) {
			/*
			 * Generate the maze. _SetSeed is used to ensure specific chunks look the same for all players in an instance.
			 * Its also called before every time the next batch of iterations is processed to produce consistent results
			 * even when the world generator is generating multiple chunks concurrently, which all use the same RNG instance.
			 * (This would also be a lot easier if Udon allowed us to instantiate custom classes outside of Object.Instantiate on a GO)
			 */
			mazeGen._SetSeed(posX * 13263126 + posY * 2154135 + worldGen.worldSeed + currBuildIter * 10413561);
			for(int i = 0; i < worldGen.generationIters; i++) {
				currBuildIter++;
				mazeGen._Gen();
				if(mazeGen._IsDone()) { //If done, move on to the next generation step
					currStep = 2;
					currMesh = 0;
					currBuildIter = 0;
					DEBUGtotalFaces = 0;
					worldGen.chunksGenerating--;
					mazeGen._DebugRender();
					return;
				}
			}
		}
		/*
		 * This code generates one sub-mesh at a time, jumping back and forth between steps 2 and 3 until all meshes are generated.
		 * Separate iteration counters are used for each step, as the counters keep track of the iterations for the whole chunk,
		 * not just the current sub-mesh.
		 * See function descriptions for InitMeshBuilder and BuildMesh for more details on how the generation works.
		 */
		if(currStep == 2) {
			InitMeshBuilder();
			if(currInitIter % meshSize == 0) {
				currStep = 3;
				return;
			}
		}
		if(currStep == 3) {
			BuildMesh();
			if(currBuildIter % meshSize == 0) {
				if(currMesh == meshCount - 1) { //All sub-meshes have been generated. It's done!
					currStep = 0;
					Debug.Log(DEBUGtotalFaces + " total faces generated");
					return;
				}else {
					currMesh++;
					currStep = 2;
					return;
				}
			}
		}
	}

	private bool IsPlayerAdjacent() {
		if((playerPos.x == posX && playerPos.y == posY + 1) || (playerPos.x == posX && playerPos.y == posY - 1) || (playerPos.y == posY && playerPos.x == posX + 1) || (playerPos.y == posY && playerPos.x == posX - 1)) return true;
		if(playerPos.x == posX && playerPos.y == posY) return true;
		return false;
	}

	/*
	 * Fires if the chunk moved to a new position. Clears its mesh and marks this chunk as needing to be generated.
	 * Also sets the chunk's position in the world.
	 */
	private void OnPositionChanged() {
		transform.localScale = new Vector3(worldGen.chunkSize / 2.0f, worldGen.chunkSize / 2.0f, worldGen.chunkSize / 2.0f);
		transform.localPosition = new Vector3(posX * worldGen.chunkSize, 0, posY * worldGen.chunkSize);
		for(int i = 0; i < meshCount; i++) componentMeshes[i].Clear();
		needsGeneration = true;
	}
	
	/*
	 * MESH BUILDER STARTS HERE
	 * 
	 * More code that's needlessly ugly and convoluted due to Udon limitations. Poor C#...they crippled it! Why is it still here? Just to suffer?
	 */
	private Vector3[] verts,normals;
	private Vector2[] uvs;
	private int[] triangles;
	private int pntr;
	private int tPntr;
	private int totalFaces;
	private int DEBUGtotalFaces;

	private Vector3 n1 = new Vector3(0, -1, 0);
	private Vector3 n2 = new Vector3(0, 1, 0);
	private Vector3 n3 = new Vector3(-1, 0, 0);
	private Vector3 n4 = new Vector3(1, 0, 0);
	private Vector2 uv00 = new Vector2(0f, 0f);
	private Vector2 uv10 = new Vector2(1f, 0f);
	private Vector2 uv01 = new Vector2(0f, 1f);
	private Vector2 uv11 = new Vector2(1f, 1f);

	/*
	 * Adds a face onto the mesh. Probably the least convoluted piece of code in this section.
	 * 
	 * direction: 0 = forward, 1 = back, 2 = right, 3 = left
	 */
	private void AddFace(float px, float pz, float wallHeight, float wallLength, float cellLength, float offset, int direction, float uvScale) {
		Vector3 normalToUse = n1;
		if(direction == 0) {
			verts[pntr    ] = new Vector3(px             , pz + offset, wallHeight);
			verts[pntr + 1] = new Vector3(wallLength + px, pz + offset, wallHeight);
			verts[pntr + 2] = new Vector3(wallLength + px, pz + offset, 0);
			verts[pntr + 3] = new Vector3(px             , pz + offset, 0);
			normalToUse = n1;
		}else if(direction == 1) {
			verts[pntr    ] = new Vector3(wallLength + px, pz + cellLength + offset, wallHeight);
			verts[pntr + 1] = new Vector3(px             , pz + cellLength + offset, wallHeight);
			verts[pntr + 2] = new Vector3(px             , pz + cellLength + offset, 0);
			verts[pntr + 3] = new Vector3(wallLength + px, pz + cellLength + offset, 0);
			normalToUse = n2;
		}else if(direction == 2) {
			verts[pntr    ] = new Vector3(px + offset, wallLength + pz, wallHeight);
			verts[pntr + 1] = new Vector3(px + offset, pz             , wallHeight);
			verts[pntr + 2] = new Vector3(px + offset, pz             , 0);
			verts[pntr + 3] = new Vector3(px + offset, wallLength + pz, 0);
			normalToUse = n3;
		}else if(direction == 3) {
			verts[pntr    ] = new Vector3(px + cellLength + offset, pz             , wallHeight);
			verts[pntr + 1] = new Vector3(px + cellLength + offset, wallLength + pz, wallHeight);
			verts[pntr + 2] = new Vector3(px + cellLength + offset, wallLength + pz, 0);
			verts[pntr + 3] = new Vector3(px + cellLength + offset, pz             , 0);
			normalToUse = n4;
		}
		normals[pntr    ] = normalToUse;
		normals[pntr + 1] = normalToUse;
		normals[pntr + 2] = normalToUse;
		normals[pntr + 3] = normalToUse;
		Vector2 mul = new Vector2(uvScale, 1);
		uvs[pntr    ] = uv11 * mul;
		uvs[pntr + 1] = uv01 * mul;
		uvs[pntr + 2] = uv00 * mul;
		uvs[pntr + 3] = uv10 * mul;
		triangles[tPntr    ] = 3 + pntr;
		triangles[tPntr + 1] = 1 + pntr;
		triangles[tPntr + 2] = 0 + pntr;
		triangles[tPntr + 3] = 3 + pntr;
		triangles[tPntr + 4] = 2 + pntr;
		triangles[tPntr + 5] = 1 + pntr;
		pntr += 4;
		tPntr += 6;
	}

	/*
	 * Because arrays are being used to store the mesh data, the lengths of those arrays must exactly fit the number of tris for the current sub-mesh.
	 * This function counts the number of faces that this part of the maze contains.
	 * This is split over multiple frames, because Udon is so slow, even a simple thing like this lags the game.
	 * After its done, it creates the arrays.
	 */
	private void InitMeshBuilder() {
		int mazeSize = mazeGen.mazeSize;
		int mx = currMesh % widthInMeshes;
		int my = currMesh / widthInMeshes;

		int cntr = 0;
		if((currInitIter % meshSize) == 0) totalFaces = 0;
		int targ = (mx + 1) * meshSize;
		for(int i = currInitIter % mazeGen.mazeSize; i < targ; i++) { //Only consider the x-coordinates inside this sub-mesh, but also ensure the iteration counter is used to not exceed the maximum allowed iterations per frame.
			if(cntr == worldGen.initIters) return;
			cntr++;
			currInitIter++;
			for(int j = my * meshSize; j < (my + 1) * meshSize; j++) { //Only consider the y-coordinates inside this sub-mesh
				int cell = mazeGen.maze[i * mazeSize + j];
				if((cell & 0b0001) != 0) {
					totalFaces++;
					int nextCell = i == mazeSize - 1 ? 0 : mazeGen.maze[(i + 1) * mazeSize + j];
					if((nextCell & 0b0001) == 0 && (nextCell & 0b0100) == 0) totalFaces++;
					nextCell = i == 0 ? 0 : mazeGen.maze[(i - 1) * mazeSize + j];
					if((nextCell & 0b0001) == 0 && (nextCell & 0b1000) == 0) totalFaces++;
				}
				if((cell & 0b0010) != 0) totalFaces++;
				if((cell & 0b0100) != 0) {
					totalFaces++;
					int nextCell = j == mazeSize - 1 ? 0 : mazeGen.maze[i * mazeSize + j + 1];
					if((nextCell & 0b0100) == 0 && (nextCell & 0b0001) == 0) totalFaces++;
					nextCell = j == 0 ? 0 : mazeGen.maze[i * mazeSize + j - 1];
					if((nextCell & 0b0100) == 0 && (nextCell & 0b0010) == 0) totalFaces++;
				}
				if((cell & 0b1000) != 0) totalFaces++;
			}
		}
		DEBUGtotalFaces += totalFaces;
		componentMeshes[currMesh].Clear();
		verts   = new Vector3[totalFaces * 4];
		normals = new Vector3[totalFaces * 4];
		uvs     = new Vector2[totalFaces * 4];
		triangles   = new int[totalFaces * 6];
		pntr = 0;
		tPntr = 0;
	}

	/*
	 * This function gradually builds up the current sub-mesh, slowly filling the mesh data arrays over many frames.
	 * Its mostly the same loop as the above function, except instead of just counting the faces, it makes use of the AddFace function
	 * to actually place them.
	 * After its done, it applies the mesh data and updates the mesh collider.
	 */
	private void BuildMesh() {
		int mazeSize = mazeGen.mazeSize;
		int mx = currMesh % widthInMeshes;
		int my = currMesh / widthInMeshes;

		float length = 2.0f / (float)mazeSize;
		float thickness = length * 0.1f * wallThicknessMultiplier;
		float uvScale = 0.1f * wallThicknessMultiplier; //The segments are all the same length, so all use identical UVs. The exception is the thin strips connecting opposing walls, which needs different UVs for the textures to look right.
		float wallHeightFixed = wallHeight / (worldGen.chunkSize / 2.0f);
		int cntr = 0;
		int targ = (mx + 1) * meshSize;
		for(int i = currBuildIter % mazeGen.mazeSize; i < targ; i++) {
			if(cntr == worldGen.buildIters) return;
			cntr++;
			currBuildIter++;
			float px = ((i - mx * meshSize) - (mazeSize / 2)) * length;
			for(int j = my * meshSize; j < (my + 1) * meshSize; j++) {
				float pz = ((j - my * meshSize) - (mazeSize / 2)) * length;
				int cell = mazeGen.maze[i * mazeSize + j];
				if((cell & 0b0001) != 0) {
					AddFace(px, pz, wallHeightFixed, length, length, 0, 0, 1);

					int nextCell = i == mazeSize - 1 ? 0 : mazeGen.maze[(i + 1) * mazeSize + j];
					if((nextCell & 0b0001) == 0 && (nextCell & 0b0100) == 0) {
						AddFace(px, pz, wallHeightFixed, thickness, length, 0, 3, uvScale);
					}
					nextCell = i == 0 ? 0 : mazeGen.maze[(i - 1) * mazeSize + j];
					if((nextCell & 0b0001) == 0 && (nextCell & 0b1000) == 0) {
						AddFace(px, pz, wallHeightFixed, thickness, length, 0, 2, uvScale);
					}
				}
				if((cell & 0b0010) != 0) {
					AddFace(px, pz, wallHeightFixed, length, length, thickness, 1, 1);
				}
				if((cell & 0b0100) != 0) {
					AddFace(px, pz, wallHeightFixed, length, length, 0, 2, 1);

					int nextCell = j == mazeSize - 1 ? 0 : mazeGen.maze[i * mazeSize + (j + 1)];
					if((nextCell & 0b0100) == 0 && (nextCell & 0b0001) == 0) {
						AddFace(px, pz, wallHeightFixed, thickness, length, 0, 1, uvScale);
					}
					nextCell = j == 0 ? 0 : mazeGen.maze[i * mazeSize + (j - 1)];
					if((nextCell & 0b0100) == 0 && (nextCell & 0b0010) == 0) {
						AddFace(px, pz, wallHeightFixed, thickness, length, 0, 0, uvScale);
					}
				}
				if((cell & 0b1000) != 0) {
					AddFace(px, pz, wallHeightFixed, length, length, thickness, 3, 1);
				}
			}
		}
		
		componentMeshes[currMesh].vertices = verts;
		componentMeshes[currMesh].triangles = triangles;
		componentMeshes[currMesh].normals = normals;
		componentMeshes[currMesh].uv = uvs;
		componentMeshes[currMesh].Optimize();
		componentMeshes[currMesh].RecalculateNormals();
		componentMeshColliders[currMesh].sharedMesh = componentMeshes[currMesh];
	}
}
