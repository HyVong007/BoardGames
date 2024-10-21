using System.Runtime.CompilerServices;
using UnityEngine;


namespace BoardGames.KingChess
{
	[RequireComponent(typeof(SpriteRenderer))]
	public sealed class Piece : MonoBehaviour
	{
		[field: SerializeField] public Color color { get; private set; }
		[field: SerializeField] public new PieceName name { get; private set; }
		[SerializeField][HideInInspector] private SpriteRenderer spriteRenderer;
		private void Reset() => spriteRenderer = GetComponent<SpriteRenderer>();


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void IncreaseSortOrder() => ++spriteRenderer.sortingOrder;


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void DecreaseSortOrder() => --spriteRenderer.sortingOrder;


		public override string ToString() => $"({color}, {name})";
	}
}