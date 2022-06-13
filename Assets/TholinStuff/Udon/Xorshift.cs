using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/*
 * Generic xorshit+ implementation in Udon. At the time of writing this, Random.InitState is broken in Udon, so this class is required to have a seedable RNG.
 * For more information on this algorithm, see https://en.wikipedia.org/wiki/Xorshift
 */
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class Xorshift : UdonSharpBehaviour {
	private ulong stateHi = 326264246;
	private ulong stateLo = 235632513;
	
	public ulong Next() {
		ulong t = stateLo;
		ulong s = stateHi;
		stateLo = s;
		t ^= t << 23;
		t ^= t >> 18;
		t ^= s ^ (s >> 5);
		stateHi = t;
		return (t + s) >> 2; //Get rid of any possible linearities in the least-significant bit. 62 bits of random number is still good enough for most applications anyways.
	}

	//Identical to Unity's Random.value. Returns a random value within [0..1] (range is inclusive).
	public float NextFloat() {
		float n = (float)(Next() & 16777215);
		return n / 16777215.0f;
	}
	
	//Same as above, but returns a double-precission value.
	public double NextDouble() {
		double n = (double)(Next() & 16777215);
		return n / 16777215.0;
	}
	
	//Returns a random integer within [0..max-1] (range is inclusive). Returns 0 if argument is invalid (< 1).
	public int NextInt(int max) {
		if(max <= 1) return 0;
		int n = (int)(Next() & 0x0FFFFFFF);
		return (int)(n % max);
	}
	
	//Returns a normally distributed value with mean 0 and std. dev. of 1.
	public float NextGaussian() {
		float u1 = 1 - NextFloat();
		float u2 = 1 - NextFloat();
		return Mathf.Sqrt(-2 * Mathf.Log(u1) * Mathf.Sin(2 * Mathf.PI * u2));
	}

	//Initialization algorithm borrowed from xoshiro to prevent state = 0
	public void SetState(ulong state) {
		ulong temp = state + 0x9E3779B97f4A7C15;
		temp = (temp ^ (temp >> 30)) * 0xBF58476D1CE4E5B9;
		temp = (temp ^ (temp >> 27)) * 0x94D049BB133111EB;
		temp = temp ^ (temp >> 31);
		stateLo = temp;
		
		temp = state + 0x9E3779B97f4A7C15 + 0x9E3779B97f4A7C15;
		temp = (temp ^ (temp >> 30)) * 0xBF58476D1CE4E5B9;
		temp = (temp ^ (temp >> 27)) * 0x94D049BB133111EB;
		temp = temp ^ (temp >> 31);
		stateHi = temp;
	}
}
