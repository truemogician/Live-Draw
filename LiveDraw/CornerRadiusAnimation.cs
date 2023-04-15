using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AntFu7.LiveDraw; 

internal class CornerRadiusAnimation : AnimationTimeline {
	static CornerRadiusAnimation() {
		FromProperty = DependencyProperty.Register("From", typeof(CornerRadius), typeof(CornerRadius));
		ToProperty = DependencyProperty.Register("To", typeof(CornerRadius), typeof(CornerRadius));
	}

	private bool _fromSet;

	private bool _toSet;

	public static readonly DependencyProperty FromProperty;

	public CornerRadius From {
		get => (CornerRadius)GetValue(FromProperty);
		set {
			SetValue(FromProperty, value);
			_fromSet = true;
		}
	}

	public static readonly DependencyProperty ToProperty;

	public CornerRadius To {
		get => (CornerRadius)GetValue(ToProperty);
		set {
			SetValue(ToProperty, value);
			_toSet = true;
		}
	}

	public override object GetCurrentValue(
		object defaultOriginValue,
		object defaultDestinationValue,
		AnimationClock animationClock
	) {
		var fromVal = _fromSet ? (CornerRadius)GetValue(FromProperty) : (CornerRadius)defaultOriginValue;
		var toVal = _toSet ? (CornerRadius)GetValue(ToProperty) : (CornerRadius)defaultDestinationValue;
		if (animationClock.CurrentProgress != null)
			return new CornerRadius(
				animationClock.CurrentProgress.Value * (toVal.TopLeft - fromVal.TopLeft) + fromVal.TopLeft,
				animationClock.CurrentProgress.Value * (toVal.TopRight - fromVal.TopRight) + fromVal.TopRight,
				animationClock.CurrentProgress.Value * (toVal.BottomRight - fromVal.BottomRight) + fromVal.BottomRight,
				animationClock.CurrentProgress.Value * (toVal.BottomLeft - fromVal.BottomLeft) + fromVal.BottomLeft
			);
		return new CornerRadius();
	}

	protected override Freezable CreateInstanceCore() => new CornerRadiusAnimation();

	public override Type TargetPropertyType => typeof(CornerRadius);
}