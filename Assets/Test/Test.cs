using BoardGames;
using UnityEngine;
using H = BoardGames.History<X, Data>;



public class Test : MonoBehaviour
{
	private void Start()
	{
		var x = H.Mode.Play;
	}
}



public struct Data : IMoveData<X>
{
	public X playerID => throw new System.NotImplementedException();
}


public enum X { A, B }