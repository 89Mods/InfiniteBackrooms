using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * Modified "Recursive division" maze generation algorithm: https://en.wikipedia.org/wiki/Maze_generation_algorithm#Recursive_division_method
 * The original would generate a maze with only one possible solution, which is not optimal here as it would mean players getting stuck in a chunk until they found the one, single path to lead them to the chunk border.
 * Modifications include:
 *  - Increased number of gaps in walls. Up to three, depending on length of the wall.
 *  - Once the segments reach below a small size, there is a random chance for the "recursion" to be aborted early, leaving a larger empty segment that isn't subdivided further, giving the appearance of a large room.
 *  - Not actually using recursion, but a stack, so the code can run in a while-loop who's execution can be split over multiple frames.
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MazeGenerator : UdonSharpBehaviour {
	//This shouldn't be changed, as most of the code works best with this maze size, especially with the default chunk size.
	public int mazeSize = 64;
	/*
	 * The actual maze data.
	 * Contains a 2D grid of cells (compressed into a flat array). Every cells uses its first 4 bits to keep track of the state of each of its 4 walls.
	 * A one-bit means a wall is present. The maze starts out with no walls, and the algorithm populates this over many iterations.
	 * Unfortunately, Udon does not permit creating a 2D-array, so this needs to be 1D-array,
	 * and some additional code is needed to convert a x, y coordinate into an array index.
	 */
	[HideInInspector] public int[] maze;
	[HideInInspector] public Texture2D tex;
	public Xorshift rng;

	private bool hasSpawnRoom = false;
	
	//The stack. Fixed size of 32. Should be fine for mazes up to 128x128 in size.
	private int    stackPtr = -5;
	private int[]  s_pos = new int[32 * 2];
	private int[]  s_dim = new int[32 * 2];
	private int[]  s_div = new int[32];
	private bool[] s_vertical = new bool[32];
	private bool[] s_needsBisection = new bool[32];

	//Initialize the generator. This resets the stack and pushes one random element onto it as a starting point
	public void _Init(bool r) {
		hasSpawnRoom = r;
		maze = new int[mazeSize * mazeSize];
		s_pos[0] = 0;
		s_pos[1] = 0;
		s_dim[0] = mazeSize;
		s_dim[1] = mazeSize;
		s_div[0] = rng.NextInt(mazeSize - 1) + 1;
		s_vertical[0] = false;
		s_needsBisection[0] = false;
		stackPtr = 1;
		if(tex != null) {
			for(int i = 0; i < 512; i++) for(int j = 0; j < 512; j++) {
				tex.SetPixel(i, j, Color.white);
			}
			tex.Apply();
		}
	}

	public void _SetSeed(int seed) {
		if(seed < 0) seed = 2147483647 + seed;
		rng.SetState((uint)seed);
	}

	//Some debug code to render the maze to a texture. Note that this functionally is exposed not here, but through the WorldGenerator class.
	public void _DebugRender() {
		if(tex != null) {
			for(int i = 0; i < mazeSize; i++) {
				for(int j = 0; j < mazeSize; j++) {
					int cell = maze[i * mazeSize + j];
					if((cell & 0b0001) != 0) {
						for(int i2 = 0; i2 < 4; i2++) tex.SetPixel(i * 4 + i2, j * 4, Color.black);
					}
					if((cell & 0b0010) != 0) {
						for(int i2 = 0; i2 < 4; i2++) tex.SetPixel(i * 4 + i2, j * 4 + 4, Color.black);
					}
					if((cell & 0b0100) != 0) {
						for(int j2 = 0; j2 < 4; j2++) tex.SetPixel(i * 4, j * 4 + j2, Color.black);
					}
					if((cell & 0b1000) != 0) {
						for(int j2 = 0; j2 < 4; j2++) tex.SetPixel(i * 4 + 4, j * 4 + j2, Color.black);
					}
				}
			}
			tex.Apply();
		}
	}
	
	/*
	 * Run one iteration of the algorithm. This can be called in a loop inside world generation code until the maze is complete.
	 * 
	 * WARNING: Convoluted stack management code starts here. Do not read further if you value your sanity!
	 * This would look a lot nicer if we could use structs in U# AND the Stack class was exposed to Udon. For now, I have to do this like its C89.
	 */
	public void _Gen() {
		if(stackPtr > 0) {
			stackPtr--;
			int sp2 = stackPtr * 2;
			//If a segment still needs to be bisected, the location of the wall bisecting it is generated here.
			if(s_needsBisection[stackPtr]) {
				s_vertical[stackPtr] = s_dim[sp2] > s_dim[sp2 + 1];
				if(s_vertical[stackPtr]) {
					s_div[stackPtr] = rng.NextInt(s_dim[sp2] - 1) + 1;
				}else {
					s_div[stackPtr] = rng.NextInt(s_dim[sp2 + 1] - 1) + 1;
				}
				s_needsBisection[stackPtr] = false;
				stackPtr++;
				return;
			}
			/*
			 * Otherwise, actually place the wall into the maze. Then, pop the segment off of the stack, and divide it, pushing the resulting component segments onto the stack.
			 * In most cases, this division resuls in two segments, each on the opposite side of the newly placed wall.
			 * But there are a few conditions in which a new segment cannot be created, causing a decrease in stack size and, ultimately, an end to the algorithm's execution.
			 */
			if(s_vertical[stackPtr]) { //Vertical segment
				int x = s_pos[sp2];
				int y = s_pos[sp2 + 1];
				int h = s_dim[sp2 + 1];
				int gapLoc1 = rng.NextInt(h) + y;
				int gapLoc2 = rng.NextInt(h) + y;
				int gapLoc3 = -10000;
				if(h >= 32) gapLoc3 = rng.NextInt(h) + y;
				int ll = y + h;
				int idx1 = x + s_div[stackPtr];
				int idx2 = idx1 * mazeSize;
				int idx3 = idx2 - mazeSize;
				for(int i = y; i < ll; i++) {
					if(i == gapLoc1 || i == gapLoc2 || i == gapLoc3) continue;
					maze[idx2 + i] |= 0b0100;
					if(idx1 > 0) maze[idx3 + i] |= 0b1000;
				}

				Divide(true);
			}else { //Horizontal segment
				int x = s_pos[sp2];
				int y = s_pos[sp2 + 1];
				int w = s_dim[sp2];
				int gapLoc1 = rng.NextInt(w) + x;
				int gapLoc2 = rng.NextInt(w) + x;
				int gapLoc3 = -10000;
				if(w >= 32) gapLoc3 = rng.NextInt(w) + x;
				int ll = x + w;
				int idx1 = y + s_div[stackPtr];
				for(int i = x; i < ll; i++) {
					if(i == gapLoc1 || i == gapLoc2 || i == gapLoc3) continue;
					maze[i * mazeSize + idx1] |= 0b0001;
					if(idx1 > 0) maze[i * mazeSize + idx1 - 1] |= 0b0010;
				}

				Divide(false);
			}
		}else if(stackPtr == 0) {
			stackPtr = -1;
			//Finish up
			if(hasSpawnRoom) { //Clear a small area in the center of the chunk as a "spawn room".
				for(int i = (mazeSize / 2) - 3; i < (mazeSize / 2) + 3; i++) {
					for(int j = (mazeSize / 2) - 5; j < (mazeSize / 2) + 5; j++) {
						maze[i * mazeSize + j] = 0;
					}
				}
			}
		}
	}

	//Returns true if the maze is done generating, and further calls to "_Gen" are going to have no effect.
	public bool _IsDone() {
		return stackPtr < 0;
	}

	//Attempt to divide the segment on top of the stack in two.
	private void Divide(bool orientation) {
		int idx = stackPtr;
		int idx2 = idx * 2;
		int sp2 = stackPtr * 2;
		if(orientation) { //Vertical
			int x = s_pos[idx2];
			int y = s_pos[idx2 + 1];
			int w = s_dim[idx2];
			int h = s_dim[idx2 + 1];
			int div = s_div[idx];
			if(h < 3) return; //Exit if resulting segments would both be too small to be divided further.
			if(div >= 3 /* Size check */) { //Left segment
				int d = System.Math.Min(div, h);
				if(!(d < 8 && rng.NextInt(d * 2) == 0)) { //Randomly abort division if the segment is small enough, to leave a larger room.
					s_pos[sp2] = x;
					s_pos[sp2 + 1] = y;
					s_dim[sp2] = div;
					s_dim[sp2 + 1] = h;
					s_needsBisection[stackPtr] = true;
					stackPtr++;
					sp2 += 2;
				}
			}

			if(w - div >= 3 /* Size check */) { //Right segment
				int d = System.Math.Min(w - div, h);
				if(!(d < 8 && rng.NextInt(d * 2) == 0)) {
					s_pos[sp2] = x + div;
					s_pos[sp2 + 1] = y;
					s_dim[sp2] = w - div;
					s_dim[sp2 + 1] = h;
					s_needsBisection[stackPtr] = true;
					stackPtr++;
				}
			}
		}else { //Horizontal
			int x = s_pos[idx2];
			int y = s_pos[idx2 + 1];
			int w = s_dim[idx2];
			int h = s_dim[idx2 + 1];
			int div = s_div[idx];
			if(w < 3) return; //Exit if resulting segments would both be too small to be divided further.
			if(div >= 3 /* Size check */) { //Bottom segment
				int d = System.Math.Min(div, w);
				if(!(d < 8 && rng.NextInt(d * 2) == 0)) {
					s_pos[sp2] = x;
					s_pos[sp2 + 1] = y;
					s_dim[sp2] = w;
					s_dim[sp2 + 1] = div;
					s_needsBisection[stackPtr] = true;
					stackPtr++;
					sp2 += 2;
				}
			}
			if(h - div >= 3 /* Size check */) { //Top segment
				int d = System.Math.Min(h - div, w);
				if(!(d < 8 && rng.NextInt(d * 2) == 0)) {
					s_pos[sp2] = x;
					s_pos[sp2 + 1] = y + div;
					s_dim[sp2] = w;
					s_dim[sp2 + 1] = h - div;
					s_needsBisection[stackPtr] = true;
					stackPtr++;
				}
			}
		}
	}
}
