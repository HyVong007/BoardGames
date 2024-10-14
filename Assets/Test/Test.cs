using BoardGames;
using UnityEngine;



public class Test : MonoBehaviour
{
	private void Start()
	{
		var x = new ReadOnlyArray<int>(new[] { 1, 2, 3 });
		Util.Random(x);
	}
}



public struct Data : IMoveData<X>
{
	public X playerID => throw new System.NotImplementedException();
}


public enum X { A, B }