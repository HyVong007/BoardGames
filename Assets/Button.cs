using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
#if UNITY_EDITOR
using UnityEditor;
#endif


namespace BoardGames
{
	/// <summary>
	/// Nhận các event: click, double click, long press, drag<br/>
	/// Tùy chỉnh thông số với mỗi loại event<para/>
	/// Chú ý: Khi vừa gắn vô gameObject hoặc khi Reset thì sẽ cache <see cref="ScrollRect"/> và xử lý Drag
	/// </summary>
#if UNITY_EDITOR
	[CanEditMultipleObjects]
#endif
	public sealed class Button : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
		IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		#region Cài đặt thông số
		[Flags]
		public enum Event
		{
			/// <summary>
			/// Nhấn -> thả <para/>
			/// Thời gian từ lúc nhấn đến khi thả phải &lt;= <see cref="clickMaxTime"/><br/>
			/// Và bình phương khoảng cách giữa điểm nhấn và điểm thả phải &lt;= <see cref="clickMaxSqrDistance"/>
			/// </summary>
			Click = 1 << 0,
			/// <summary>
			/// Click 2 lần liên tiếp: Nhấn -> thả -> nhấn -> thả <para/>
			/// Thời gian từ lúc thả ở click 1 đến khi nhấn ở click 2 phải &lt;= <see cref="clickMaxTime"/><br/>
			/// Bình phương khoảng cách giữa điểm thả ở click 1 và điểm nhấn ở click 2 phải &lt;= <see cref="clickMaxSqrDistance"/>
			/// </summary>
			DoubleClick = 1 << 1,
			/// <summary>
			/// Nhấn liên tục trong thời gian &gt;= <see cref="longPressMinTime"/> <br/>
			/// Cho phép di chuyển với bình phương khoảng cách &lt;= <see cref="clickMaxSqrDistance"/>
			/// </summary>
			LongPress = 1 << 2,
			/// <summary>
			/// Kéo từ vị trí A sang vị trí B và liên tục cập nhật vị trí.<br/>
			/// Có 2 chế độ:<para/>
			/// <see cref="DragType.Default"/>: Nhấn -> kéo trong khi đang nhấn -> thả<br/>
			/// <see cref="DragType.TwoPress"/>: Nhấn -> di chuyển không cần nhấn giữ -> nhấn
			/// </summary>
			Drag = 1 << 3
		}

		[SerializeField]
		[ValidateInput("Validate_event")]
		private Event m_event;
#if UNITY_EDITOR
		private Event ΔeditorBaking;
#endif
		public Event @event
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_event;

			set
			{
				if (value == m_event
#if UNITY_EDITOR
					&& value == ΔeditorBaking
#endif
					) return;

#if UNITY_EDITOR
				ΔeditorBaking =
#endif
				m_event = value;
				lastClick = null;
				cancelLongPress?.Cancel();
				cancelLongPress?.Dispose();
				cancelLongPress = null;
				drag = false;
			}
		}


		[Tooltip("Thời gian (giây) tối đa cho phép từ lúc nhấn đến khi thả để được xem là click")]
		[SerializeField]
		[Range(0.01f, 5)]
		[ShowIf("show_clickMaxTime")]
		private float clickMaxTime;

		[Tooltip("Bình phương khoảng cách tối đa cho phép giữa điểm nhấn và điểm thả để được xem là click")]
		[SerializeField]
		[Range(0, 500 * 500)]
		[ShowIf("show_clickMaxSqrDistance")]
		private float clickMaxSqrDistance;

		[Tooltip("Thời gian (giây) tối thiểu khi nhấn giữ để được xem là Long Press")]
		[SerializeField]
		[Range(1, 7)]
		[ShowIf("show_longPressMinTime")]
		[ValidateInput("Validate_longPressMinTime")]
		private float longPressMinTime;

		public enum DragType
		{
			/// <summary>
			/// Nhấn -> kéo trong khi đang nhấn -> thả
			/// </summary>
			Default,
			/// <summary>
			/// Nhấn -> di chuyển không cần nhấn giữ -> nhấn
			/// </summary>
			TwoPress
		}

		[SerializeField]
		[Tooltip("Default: Nhấn -> kéo trong khi đang nhấn -> thả. TwoPress: Nhấn -> di chuyển không cần nhấn giữ -> nhấn")]
		[ShowIf("show_dragType")]
		private DragType m_dragType;
		public DragType dragType
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => m_dragType;
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set => m_dragType = value;
		}


#if UNITY_EDITOR
		private bool show_clickMaxSqrDistance => (@event & (Event.Click | Event.DoubleClick | Event.LongPress)) != 0;

		private bool show_clickMaxTime => (@event & (Event.Click | Event.DoubleClick)) != 0;

		private bool show_longPressMinTime => (@event & Event.LongPress) != 0;

		private bool show_dragType => (@event & Event.Drag) != 0;


		private bool Validate_event(Event _)
		{
			if (Application.isPlaying) @event = @event;
			return true;
		}


		private bool Validate_longPressMinTime(float _)
		{
			if (longPressMinTime <= clickMaxTime) longPressMinTime = clickMaxTime + 1;
			return true;
		}
#endif
		#endregion


		#region Drag
		/// <summary>
		/// Nhập vào pixel, trả về kết quả:<br/>
		/// Tất cả handler trả về <see langword="true"/> thì tiếp tục drag<br/>
		/// Tồn tại ít nhất 1 handler trả về <see langword="false"/> thì kết thúc drag (gọi <see cref="endDrag"/>)
		/// </summary>
		public event Func<Vector2, bool> beginDrag
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			add { list_beginDrag.Add(value); }
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			remove { list_beginDrag.Remove(value); }
		}

		/// <summary>
		/// Nhập vào pixel, trả về kết quả:<br/>
		/// Tất cả handler trả về <see langword="true"/> thì tiếp tục drag<br/>
		/// Tồn tại ít nhất 1 handler trả về <see langword="false"/> thì kết thúc drag (gọi <see cref="endDrag"/>)
		/// </summary>
		public event Func<Vector2, bool> dragging
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			add { list_dragging.Add(value); }
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			remove { list_dragging.Remove(value); }
		}

		/// <summary>
		/// Tọa độ pixel
		/// </summary>
		public event Action<Vector2> endDrag;

		private readonly List<Func<Vector2, bool>> list_beginDrag = new(), list_dragging = new();

		private CancellationTokenSource cts;

		private bool Δdrag;
		private bool drag
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Δdrag;

			set
			{
				if (Δdrag == value) return;

				Δdrag = value;
				var pointerPosition = this.pointerPosition;
				if (value)
				{
					foreach (var f in list_beginDrag) value &= f(pointerPosition);
					if (!value)
					{
						endDrag?.Invoke(pointerPosition);
						return;
					}

					Touch.onFingerMove += OnFinger;
					if (Mouse.current == null) return;

					cts = new();
					CheckMouseMove().Forget();
				}
				else
				{
					endDrag?.Invoke(pointerPosition);
					Touch.onFingerMove -= OnFinger;
					cts?.Cancel();
					cts?.Dispose();
					cts = null;
				}


				void OnFinger(Finger f) => Dragging(f.screenPosition);


				async UniTask CheckMouseMove()
				{
					var token = cts.Token;
					var pos = new Vector2(Mouse.current.position.x.ReadValue(), Mouse.current.position.y.ReadValue());
					while (!token.IsCancellationRequested)
					{
						if (Mouse.current.wasUpdatedThisFrame)
						{
							var tmp = new Vector2(Mouse.current.position.x.ReadValue(), Mouse.current.position.y.ReadValue());
							if (tmp != pos) Dragging(pos = tmp);
						}

						await UniTask.DelayFrame(1);
					}
				}


				void Dragging(in Vector2 position)
				{
					value = true;
					foreach (var f in list_dragging) value &= f(position);
					if (!value) drag = false;
				}
			}
		}
		#endregion


		/// <summary>
		/// Tọa độ pixel
		/// </summary>
		public event Action<Vector2> click, doubleClick, longPress;
		private struct Data
		{
			public Vector2 position;
			public float downTime, upTime;
		}
		private Data? lastClick;
		private Data tmp;

		public void OnPointerDown(PointerEventData eventData)
		{
			if (@event == 0) return;
			tmp.position = eventData.position;
			tmp.downTime = Time.time;

			if ((@event & Event.DoubleClick) != 0 && lastClick != null && (
				tmp.downTime - lastClick.Value.upTime > clickMaxTime
				|| (tmp.position - lastClick.Value.position).sqrMagnitude > clickMaxSqrDistance))
				lastClick = null;

			if ((@event & Event.LongPress) != 0)
			{
				cancelLongPress = new();
				CheckLongPress(tmp).Forget();
			}

			if ((@event & Event.Drag) != 0) drag = !drag;
		}


		public void OnPointerUp(PointerEventData eventData)
		{
			if (@event == 0) return;

			if ((@event & (Event.Click | Event.DoubleClick)) != 0
				&& Time.time - tmp.downTime <= clickMaxTime
				&& (eventData.position - tmp.position).sqrMagnitude <= clickMaxSqrDistance)
			{
				tmp.upTime = Time.time;
				if ((@event & Event.Click) != 0) click?.Invoke(tmp.position);
				if ((@event & Event.DoubleClick) != 0)
					if (lastClick != null)
					{
						doubleClick?.Invoke(lastClick.Value.position);
						lastClick = null;
					}
					else lastClick = tmp;
			}

			if ((@event & Event.LongPress) != 0)
			{
				cancelLongPress.Cancel();
				cancelLongPress.Dispose();
			}

			if ((@event & Event.Drag) != 0 && dragType == DragType.Default) drag = false;
		}


		private Vector2 pointerPosition =>
			Touch.activeTouches.Count != 0 ? Touch.activeTouches[0].screenPosition
			: new(Mouse.current.position.x.ReadValue(), Mouse.current.position.y.ReadValue());


		private CancellationTokenSource cancelLongPress;
		private async UniTask CheckLongPress(Data data)
		{
			var token = cancelLongPress.Token;
			float t = data.downTime + longPressMinTime;
			while (Time.time < t)
			{
				if ((pointerPosition - data.position).sqrMagnitude > clickMaxSqrDistance) return;

				await UniTask.DelayFrame(1, cancellationToken: token);
			}

			longPress?.Invoke(data.position);
		}


		#region interactable
		[SerializeField]
		[ValidateInput("Validate_interactable")]
		[ShowIf("show_interactable")]
		private bool _interactable = true;
		[SerializeField]
		[ShowIf("show_interactable")]
		private Color activeColor = Color.white, disabledColor = new(1, 1, 1, 0.5f);
		private Graphic graphic;
#if UNITY_EDITOR
		private bool editorBaking;
#endif
		public bool interactable
		{
			get => _interactable;

			set
			{
				if (value == _interactable
#if UNITY_EDITOR
						&& value == editorBaking
#endif
						) return;

#if UNITY_EDITOR
				editorBaking =
#endif
					_interactable = value;

				enabled = value;
				graphic = graphic != null ? graphic : GetComponent<Graphic>();
				if (!graphic) return;

				graphic.raycastTarget = value;
				graphic.color = value ? activeColor : disabledColor;
			}
		}


#if UNITY_EDITOR
		private bool Validate_interactable(bool _)
		{
			interactable = interactable;
			return true;
		}


		private bool show_interactable => @event != 0;
#endif
		#endregion


		#region ScrollRect
		[HideInInspector][SerializeField] private ScrollRect scrollRect;
		private void Reset() => scrollRect = GetComponentInParent<ScrollRect>();


		public void OnBeginDrag(PointerEventData eventData)
		{
			if (scrollRect) scrollRect.OnBeginDrag(eventData);
		}


		public void OnDrag(PointerEventData eventData)
		{
			if (scrollRect) scrollRect.OnDrag(eventData);
		}


		public void OnEndDrag(PointerEventData eventData)
		{
			if (scrollRect) scrollRect.OnEndDrag(eventData);
		}
		#endregion
	}
}