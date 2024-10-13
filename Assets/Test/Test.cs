using BoardGames;
using UnityEngine;



public class Test : MonoBehaviour
{
	private void Start()
	{
		MoveType p;
	}
}



public struct Data : IMoveData<X>
{
	public X playerID => throw new System.NotImplementedException();
}


public enum X { A, B }