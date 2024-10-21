using NaughtyAttributes;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;


namespace BoardGames.ChineseChess
{
	/// <summary>
	/// Mặc định là hiện
	/// </summary>
	[RequireComponent(typeof(SpriteRenderer))]
	public sealed class PieceGUI : MonoBehaviour
	{
		[field: SerializeField] public Color color { get; private set; }
		[field: SerializeField] public new PieceName name { get; private set; }
		private SpriteRenderer spriteRenderer;


		[ShowAssetPreview][SerializeField] private Sprite hiddenSprite;
		private bool _hidden;
		/// <summary>
		/// Nếu <see langword="true"/> là úp
		/// </summary>
		public bool hidden
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _hidden;

			set
			{
				if (value == _hidden) return;
#if DEBUG
				if (name == PieceName.General && value)
					throw new InvalidOperationException("Tướng không thể úp !");
#endif
				_hidden = value;
				spriteRenderer.sprite = value ? hiddenSprite : isSymbol ? symbolSprite : normalSprite;
			}
		}


		[ShowAssetPreview][SerializeField] private Sprite symbolSprite;
		private Sprite normalSprite;
		private static readonly List<PieceGUI> pieces = new();

		private static bool _isSymbol;
		/// <summary>
		/// Nếu <see langword="true"/> là biểu tượng hình<br/>
		/// Nếu <see langword="false"/> là chữ
		/// </summary>
		public static bool isSymbol
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => _isSymbol;

			set
			{
				if (value == _isSymbol) return;
				_isSymbol = value;

				foreach (var piece in pieces)
					piece.spriteRenderer.sprite = piece.hidden ? piece.hiddenSprite
						: value ? piece.symbolSprite : piece.normalSprite;
			}
		}


		private void Awake()
		{
			normalSprite = (spriteRenderer = GetComponent<SpriteRenderer>()).sprite;
			spriteRenderer.sprite = isSymbol ? symbolSprite : normalSprite;
			pieces.Add(this);
		}


		private void OnDestroy() => pieces.Remove(this);


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void IncreaseSortOrder() => ++spriteRenderer.sortingOrder;


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void DecreaseSortOrder() => --spriteRenderer.sortingOrder;


		public override string ToString() => $"({color}, {name}, hidden= {hidden}, isSymbol= {isSymbol})";
	}
}