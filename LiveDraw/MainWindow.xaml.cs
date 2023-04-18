using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AntFu7.LiveDraw.Properties;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace AntFu7.LiveDraw;

public enum PenMode : byte {
	Arbitrary,

	Line,

	Parabola
}

public partial class MainWindow : INotifyPropertyChanged {
	private static readonly Mutex mutex = new(true, "LiveDraw");

	private static readonly Duration Duration1 = (Duration)Application.Current.Resources["Duration1"];

	private static readonly Duration Duration2 = (Duration)Application.Current.Resources["Duration2"];

	private static readonly Duration Duration3 = (Duration)Application.Current.Resources["Duration3"];

	private static readonly Duration Duration4 = (Duration)Application.Current.Resources["Duration4"];

	private static readonly Duration Duration5 = (Duration)Application.Current.Resources["Duration5"];

	private static readonly Duration Duration7 = (Duration)Application.Current.Resources["Duration7"];

	private static readonly Duration Duration10 = (Duration)Application.Current.Resources["Duration10"];

	public event PropertyChangedEventHandler PropertyChanged;

	protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null) {
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;
		field = value;
		OnPropertyChanged(propertyName);
		return true;
	}

	#region /---------Lifetime---------/
	public MainWindow() {
		if (!mutex.WaitOne(TimeSpan.Zero, true)) {
			Application.Current.Shutdown(0);
			return;
		}
		_history = new ObservableStack<StrokesHistoryNode>();
		_redoHistory = new ObservableStack<StrokesHistoryNode>();
		void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs args) {
			if (sender is not ObservableStack<StrokesHistoryNode> stack)
				throw new InvalidOperationException("Unexpected sender");
			switch (args.Action) {
				case NotifyCollectionChangedAction.Add when args.NewItems?.Count == stack.Count:
				case NotifyCollectionChangedAction.Remove when stack.Count == 0:
				case NotifyCollectionChangedAction.Reset: {
					if (stack.Equals(_history))
						OnPropertyChanged(nameof(CanUndo));
					else if (stack.Equals(_redoHistory))
						OnPropertyChanged(nameof(CanRedo));
					break;
				}
			}
		}
		_history.CollectionChanged += OnCollectionChanged;
		_redoHistory.CollectionChanged += OnCollectionChanged;

		if (!Directory.Exists("Save"))
			Directory.CreateDirectory("Save");

		InitializeComponent();
		Color = DefaultColorPicker;
		EnableDrawing = false;
		ShowDetailedPanel = true;
		Topmost = true;
		BrushIndex = 1;
		DetailPanel.Opacity = 0;

		MainInkCanvas.Strokes.StrokesChanged += StrokesChanged;
		MainInkCanvas.MouseLeftButtonDown += StartLine;
		MainInkCanvas.MouseLeftButtonUp += EndLine;
		MainInkCanvas.MouseMove += MakeLine;
		MainInkCanvas.MouseWheel += MainInkCanvas_MouseWheel;
	}

	private void MainWindow_Loaded(object sender, RoutedEventArgs e) {
		AdjustWindowSize();
		Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => AdjustWindowSize();
	}

	private void Exit(object sender, EventArgs e) {
		if (IsUnsaved)
			QuickSave("ExitingAutoSave_");

		Application.Current.Shutdown(0);
	}
	#endregion

	#region /---------Judge--------/
	private bool _saved;

	private bool IsUnsaved => MainInkCanvas.Strokes.Count != 0 && !_saved;

	private bool PromptToSave() {
		if (!IsUnsaved)
			return true;
		var r = MessageBox.Show(
			"You have unsaved work, do you want to save it now?",
			"Unsaved data",
			MessageBoxButton.YesNoCancel
		);
		switch (r) {
			case MessageBoxResult.Yes or MessageBoxResult.OK:
				QuickSave();
				return true;
			case MessageBoxResult.No:
			case MessageBoxResult.None: return true;
			default: return false;
		}
	}
	#endregion

	#region /---------Setter---------/
	private ColorPicker _color;

	private bool _hideInk = true;

	private bool _showDetailedPanel;

	private bool _eraserMode;

	private bool _enableDrawing;

	private static readonly int[] _brushSizes = { 3, 5, 8, 13, 20 };

	private int _brushIndex = 1;

	private bool _useVerticalDisplay;

	private bool ShowDetailedPanel {
		get => _showDetailedPanel;
		set {
			if (value) {
				DetailTogglerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(180, Duration5));
				DetailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Duration4));
			}
			else {
				DetailTogglerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, Duration5));
				DetailPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, Duration4));
			}
			_showDetailedPanel = value;
		}
	}

	public bool HideInk {
		get => _hideInk;
		set {
			if (_hideInk == value)
				return;
			SetField(ref _hideInk, value);
			MainInkCanvas.BeginAnimation(
				OpacityProperty,
				value ? new DoubleAnimation(0, 1, Duration3) : new DoubleAnimation(1, 0, Duration3)
			);
		}
	}

	public bool EnableDrawing {
		get => _enableDrawing;
		set {
			if (_enableDrawing == value)
				return;
			Background = Application.Current.Resources[value ? "FakeTransparent" : "TrueTransparent"] as Brush;
			SetField(ref _enableDrawing, value);
			if (value)
				SyncStatus();
			else {
				StaticInfo = "Locked";
				MainInkCanvas.EditingMode = InkCanvasEditingMode.None;
			}
		}
	}

	private ColorPicker Color {
		get => _color;
		set {
			if (ReferenceEquals(_color, value))
				return;
			if (value.Background is not SolidColorBrush brush)
				return;
			var ani = new ColorAnimation(brush.Color, Duration3);
			MainInkCanvas.DefaultDrawingAttributes.Color = brush.Color;
			BrushPreview.Background.BeginAnimation(SolidColorBrush.ColorProperty, ani);
			value.IsActivated = true;
			if (_color != null)
				_color.IsActivated = false;
			_color = value;
		}
	}

	public bool EraserMode {
		get => _eraserMode;
		set {
			if (_eraserMode == value)
				return;
			SetField(ref _eraserMode, value);
			if (value) {
				StaticInfo = "Eraser Mode";
				MainInkCanvas.UseCustomCursor = false;
				MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
			}
			else
				SyncStatus();
		}
	}

	public bool UseVerticalDisplay {
		get => _useVerticalDisplay;
		set {
			if (_useVerticalDisplay == value)
				return;
			SetField(ref _useVerticalDisplay, value);
			PaletteRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(value ? -90 : 0, Duration4));
			Palette.BeginAnimation(MinWidthProperty, new DoubleAnimation(value ? 90 : 0, Duration7));
		}
	}

	private int BrushIndex {
		get => _brushIndex;
		set {
			_brushIndex = value;
			if (_brushIndex < 0)
				_brushIndex += _brushSizes.Length;
			else if (_brushIndex >= _brushSizes.Length)
				_brushIndex -= _brushSizes.Length;
			int size = _brushSizes[_brushIndex];
			if (MainInkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint) {
				MainInkCanvas.EditingMode = InkCanvasEditingMode.GestureOnly;
				MainInkCanvas.EraserShape = new EllipseStylusShape(size, size);
				MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
			}
			else {
				MainInkCanvas.DefaultDrawingAttributes.Height = size;
				MainInkCanvas.DefaultDrawingAttributes.Width = size;
				BrushPreview?.BeginAnimation(HeightProperty, new DoubleAnimation(size, Duration4));
				BrushPreview?.BeginAnimation(WidthProperty, new DoubleAnimation(size, Duration4));
			}
		}
	}
	#endregion

	#region /---------IO---------/
	private StrokeCollection _preLoadStrokes;

	private static string GenerateFileName(string fileExt = ".fdw") => DateTime.Now.ToString("yyyyMMdd-HHmmss") + fileExt;

	private void QuickSave(string filename = "QuickSave_") => Save(new FileStream("Save\\" + filename + GenerateFileName(), FileMode.OpenOrCreate));

	private void Save(Stream fs) {
		try {
			if (fs == Stream.Null)
				return;
			MainInkCanvas.Strokes.Save(fs);
			_saved = true;
			Display("Ink saved");
			fs.Close();
		}
		catch (Exception ex) {
			MessageBox.Show(ex.ToString());
			Display("Fail to save");
		}
	}

	private StrokeCollection Load(Stream fs) {
		try {
			return new StrokeCollection(fs);
		}
		catch (Exception ex) {
			MessageBox.Show(ex.ToString());
			Display("Fail to load");
		}
		return new StrokeCollection();
	}

	private void AnimatedReload(StrokeCollection sc) {
		_preLoadStrokes = sc;
		var ani = new DoubleAnimation(0, Duration3);
		ani.Completed += LoadAniCompleted;
		MainInkCanvas.BeginAnimation(OpacityProperty, ani);
	}

	private void LoadAniCompleted(object sender, EventArgs e) {
		if (_preLoadStrokes == null)
			return;
		MainInkCanvas.Strokes = _preLoadStrokes;
		Display("Ink loaded");
		_saved = true;
		ClearHistory();
		MainInkCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Duration3));
	}
	#endregion

	#region /---------Helper---------/
	private string _staticInfo = "";

	private bool _displayingInfo;

	private string StaticInfo {
		get => _staticInfo;
		set {
			_staticInfo = value;
			if (!_displayingInfo)
				InfoBox.Text = value;
		}
	}

	private void Display(string info, int duration = 2000) {
		InfoBox.Text = info;
		_displayingInfo = true;
		Dispatcher.InvokeAsync(
			async () => {
				await Task.Delay(duration);
				_displayingInfo = false;
				InfoBox.Text = StaticInfo;
			}
		);
	}

	private static Stream SaveDialog(string initFileName, string fileExt = ".fdw", string filter = "Free Draw Save (*.fdw)|*fdw") {
		var dialog = new Microsoft.Win32.SaveFileDialog() {
			DefaultExt = fileExt,
			Filter = filter,
			FileName = initFileName,
			InitialDirectory = Directory.GetCurrentDirectory() + "Save"
		};
		return dialog.ShowDialog() == true ? dialog.OpenFile() : Stream.Null;
	}

	private static Stream OpenDialog(string fileExt = ".fdw", string filter = "Free Draw Save (*.fdw)|*fdw") {
		var dialog = new Microsoft.Win32.OpenFileDialog {
			DefaultExt = fileExt,
			Filter = filter
		};
		return dialog.ShowDialog() == true ? dialog.OpenFile() : Stream.Null;
	}

	private void SyncStatus() {
		if (EraserMode) {
			StaticInfo = "Eraser Mode";
			MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
			MainInkCanvas.UseCustomCursor = false;
		}
		else if (PenMode == PenMode.Arbitrary) {
			StaticInfo = "Draw Mode";
			MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
			MainInkCanvas.UseCustomCursor = false;
		}
		else {
			StaticInfo = $"{Enum.GetName(PenMode)} Mode";
			MainInkCanvas.EditingMode = InkCanvasEditingMode.None;
			MainInkCanvas.UseCustomCursor = true;
		}
	}

	private static double ScreenScale => Math.Max(
		Screen.PrimaryScreen!.Bounds.Width / SystemParameters.PrimaryScreenWidth,
		Screen.PrimaryScreen.Bounds.Height / SystemParameters.PrimaryScreenHeight
    );

	private void AdjustWindowSize() {
		var primaryArea = Screen.PrimaryScreen!.WorkingArea;
		var scaleRatio = ScreenScale;
		Left = Screen.AllScreens.Min(s => s.WorkingArea.Left) / scaleRatio;
		Top = Screen.AllScreens.Min(s => s.WorkingArea.Top) / scaleRatio;
		Width = Screen.AllScreens.Max(s => s.WorkingArea.Right) / scaleRatio - Left;
		Height = Screen.AllScreens.Max(s => s.WorkingArea.Bottom) / scaleRatio - Top;

		Canvas.SetTop(Palette, (primaryArea.Top + primaryArea.Height / 2.0) / scaleRatio - Top - Palette.ActualHeight / 2);
		Canvas.SetLeft(Palette, (primaryArea.Left + primaryArea.Width / 2.0) / scaleRatio - Left - Palette.ActualWidth / 2);
	}
	#endregion

	#region /---------Ink---------/
	private readonly ObservableStack<StrokesHistoryNode> _history;

	private readonly ObservableStack<StrokesHistoryNode> _redoHistory;

	private bool _ignoreStrokesChange;

	public bool CanUndo => _history.Count != 0;

	public bool CanRedo => _redoHistory.Count != 0;

	private void Undo() {
		if (!CanUndo)
			return;
		var last = _history.Pop();
		_ignoreStrokesChange = true;
		if (last.Type == StrokesHistoryNodeType.Added)
			MainInkCanvas.Strokes.Remove(last.Strokes);
		else
			MainInkCanvas.Strokes.Add(last.Strokes);
		_ignoreStrokesChange = false;
		_redoHistory.Push(last);
	}

	private void Redo() {
		if (!CanRedo)
			return;
		var last = _redoHistory.Pop();
		_ignoreStrokesChange = true;
		if (last.Type == StrokesHistoryNodeType.Removed)
			MainInkCanvas.Strokes.Remove(last.Strokes);
		else
			MainInkCanvas.Strokes.Add(last.Strokes);
		_ignoreStrokesChange = false;
		_history.Push(last);
	}

	private void StrokesChanged(object sender, StrokeCollectionChangedEventArgs e) {
		if (_ignoreStrokesChange)
			return;
		_saved = false;
		var oldStatus = (CanUndo, CanRedo);
		if (e.Added.Count != 0)
			_history.Push(new StrokesHistoryNode(e.Added, StrokesHistoryNodeType.Added));
		if (e.Removed.Count != 0)
			_history.Push(new StrokesHistoryNode(e.Removed, StrokesHistoryNodeType.Removed));
		_redoHistory.Clear();
		if (CanUndo != oldStatus.CanUndo)
			OnPropertyChanged(nameof(CanUndo));
		if (CanRedo != oldStatus.CanRedo)
			OnPropertyChanged(nameof(CanRedo));
	}

	private void ClearHistory() {
		_history.Clear();
		_redoHistory.Clear();
	}

	private void Clear() {
		MainInkCanvas.Strokes.Clear();
		ClearHistory();
	}

	private void AnimatedClear() {
		var ani = new DoubleAnimation(0, Duration3);
		ani.Completed += ClearAniComplete;
		MainInkCanvas.BeginAnimation(OpacityProperty, ani);
	}

	private void ClearAniComplete(object sender, EventArgs e) {
		Clear();
		Display("Cleared");
		MainInkCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Duration3));
	}
	#endregion

	#region /---------UI---------/
	private ColorPickerButtonSize _colorPickerSize = ColorPickerButtonSize.Small;

	public ColorPickerButtonSize ColorPickerSize {
		get => _colorPickerSize;
		set {
			if (_colorPickerSize == value)
				return;
			SetField(ref _colorPickerSize, value);
			OnPropertyChanged(nameof(ColorPickerRadius));
		}
	}

	public double ColorPickerRadius => (double)Application.Current.Resources[$"ColorPicker{Enum.GetName(ColorPickerSize)}"] / 2;

	private void DetailToggler_Click(object sender, RoutedEventArgs e) => ShowDetailedPanel = !ShowDetailedPanel;

	private void CloseButton_Click(object sender, RoutedEventArgs e) {
		Topmost = false;
		var anim = new DoubleAnimation(0, Duration3);
		anim.Completed += Exit;
		BeginAnimation(OpacityProperty, anim);
	}

	private void ColorPickers_Click(object sender, RoutedEventArgs e) {
		if (sender is not ColorPicker border)
			return;
		Color = border;
		if (EraserMode)
			EraserMode = false;
	}

	private void MainInkCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
		if (ParabolaMode) {
			AdjustParabolaFactor(e.Delta / 30);
			MakeLine(sender);
		}
		else {
			if (e.Delta < 0)
				--BrushIndex;
			else
				++BrushIndex;
		}
	}

	private void ToggleButton_Click(object sender, RoutedEventArgs e) {
		if (sender is ActivatableButton btn)
			btn.IsActivated = !btn.IsActivated;
	}

	private void ParabolaModeButton_Click(object sender, RoutedEventArgs e) {
		if (!ParabolaMode)
			ParabolaMode = true;
		else if (!ReversedParabola)
			ReversedParabola = true;
		else
			ReversedParabola = ParabolaMode = false;
	}

	private void BrushSwitchButton_Click(object sender, RoutedEventArgs e) => ++BrushIndex;

	private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

	private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

	private void ClearButton_Click(object sender, RoutedEventArgs e) => AnimatedClear();

	private void SaveButton_Click(object sender, RoutedEventArgs e) {
		if (MainInkCanvas.Strokes.Count == 0) {
			Display("Nothing to save");
			return;
		}
		QuickSave();
	}

	private void SaveButton_RightClick(object sender, MouseButtonEventArgs e) {
		if (MainInkCanvas.Strokes.Count == 0) {
			Display("Nothing to save");
			return;
		}
		Save(SaveDialog(GenerateFileName()));
	}

	private void LoadButton_Click(object sender, RoutedEventArgs e) {
		if (!PromptToSave())
			return;
		var s = OpenDialog();
		if (s == Stream.Null)
			return;
		AnimatedReload(Load(s));
	}

	private void ExportButton_Click(object sender, RoutedEventArgs e) {
		if (MainInkCanvas.Strokes.Count == 0) {
			Display("Nothing to save");
			return;
		}
		try {
			var s = SaveDialog(
				"ImageExport_" + GenerateFileName(".png"),
				".png",
				"Portable Network Graphics (*png)|*png"
			);
			if (s == Stream.Null)
				return;
			var rtb = new RenderTargetBitmap(
				(int)MainInkCanvas.ActualWidth,
				(int)MainInkCanvas.ActualHeight,
				96d,
				96d,
				PixelFormats.Pbgra32
			);
			rtb.Render(MainInkCanvas);
			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(rtb));
			encoder.Save(s);
			s.Close();
			Display("Image Exported");
		}
		catch (Exception ex) {
			MessageBox.Show(ex.ToString());
			Display("Export failed");
		}
	}

	private void ExportButton_RightClick(object sender, MouseButtonEventArgs e) {
		if (MainInkCanvas.Strokes.Count == 0) {
			Display("Nothing to save");
			return;
		}
		try {
			var s = SaveDialog("ImageExportWithBackground_" + GenerateFileName(".png"), ".png", "Portable Network Graphics (*png)|*png");
			if (s == Stream.Null)
				return;
			Palette.Opacity = 0;
			Palette.Dispatcher.Invoke(DispatcherPriority.Render, () => { });
			Thread.Sleep(100);
			var fromHwnd = Graphics.FromHwnd(IntPtr.Zero);
			var w = (int)(SystemParameters.PrimaryScreenWidth * fromHwnd.DpiX / 96.0);
			var h = (int)(SystemParameters.PrimaryScreenHeight * fromHwnd.DpiY / 96.0);
			var image = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			Graphics.FromImage(image).CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
			image.Save(s, ImageFormat.Png);
			Palette.Opacity = 1;
			s.Close();
			Display("Image Exported");
		}
		catch (Exception ex) {
			MessageBox.Show(ex.ToString());
			Display("Export failed");
		}
	}

	private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
	#endregion

	#region /---------Docking---------/
	private enum DockingDirection {
		None,

		Top,

		Left,

		Right
	}

	private const int _dockingEdgeThreshold = 30;

	private const int _dockingAwaitTime = 10000;

	private const int _dockingSideIndent = 290;

	private static void AnimatedCanvasMoving(UIElement ctr, Point to, Duration dur) {
		ctr.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(Canvas.GetTop(ctr), to.Y, dur));
		ctr.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(Canvas.GetLeft(ctr), to.X, dur));
	}

	private DockingDirection CheckDocking() {
		double left = Canvas.GetLeft(Palette);
		double right = Canvas.GetRight(Palette);
		double top = Canvas.GetTop(Palette);

		if (left is > 0 and < _dockingEdgeThreshold)
			return DockingDirection.Left;
		if (right is > 0 and < _dockingEdgeThreshold)
			return DockingDirection.Right;
		return top is > 0 and < _dockingEdgeThreshold ? DockingDirection.Top : DockingDirection.None;
	}

	private void RightDocking() => AnimatedCanvasMoving(Palette, new Point(ActualWidth + _dockingSideIndent, Canvas.GetTop(Palette)), Duration5);

	private void LeftDocking() => AnimatedCanvasMoving(Palette, new Point(0 - _dockingSideIndent, Canvas.GetTop(Palette)), Duration5);

	private void TopDocking() { }

	private async void AwaitDocking() {
		await Docking();
	}

	private Task Docking() {
		return Task.Run(
			() => {
				Thread.Sleep(_dockingAwaitTime);
				var direction = CheckDocking();
				switch (direction) {
					case DockingDirection.Left:
						LeftDocking();
						break;
					case DockingDirection.Right:
						RightDocking();
						break;
					case DockingDirection.Top:
						TopDocking();
						break;
				}
			}
		);
	}
	#endregion

	#region /---------Dragging---------/
	private Point _lastMousePosition;

	private bool _isDragging;

	private bool _tempEnable;

	private void StartDrag() {
		_lastMousePosition = Mouse.GetPosition(this);
		_isDragging = true;
		Palette.Background = new SolidColorBrush(Colors.Transparent);
		_tempEnable = EnableDrawing;
		EnableDrawing = true;
	}

	private void EndDrag() {
		if (_isDragging)
			EnableDrawing = _tempEnable;
		_isDragging = false;
		Palette.Background = null;
	}

	private void PaletteGrip_MouseDown(object sender, MouseButtonEventArgs e) => StartDrag();

	private void Palette_MouseMove(object sender, MouseEventArgs e) {
		if (!_isDragging)
			return;
		var currentMousePosition = Mouse.GetPosition(this);
		var offset = currentMousePosition - _lastMousePosition;

		Canvas.SetTop(Palette, Canvas.GetTop(Palette) + offset.Y);
		Canvas.SetLeft(Palette, Canvas.GetLeft(Palette) + offset.X);

		_lastMousePosition = currentMousePosition;
	}

	private void Palette_MouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

	private void Palette_MouseLeave(object sender, MouseEventArgs e) => EndDrag();
	#endregion

	#region /---------Shortcuts--------/
	private void Window_KeyDown(object sender, KeyEventArgs e) {
		if (e.Key == Key.R)
			EnableDrawing = !EnableDrawing;
		if (!EnableDrawing)
			return;

		switch (e.Key) {
			case Key.Z:
				Undo();
				break;
			case Key.Y:
				Redo();
				break;
			case Key.E:
				EraserMode = true;
				break;
			case Key.B:
				EnableDrawing = true;
				break;
			case Key.L:
				LineMode = true;
				break;
			case Key.Add:
				++BrushIndex;
				break;
			case Key.Subtract:
				--BrushIndex;
				break;
		}
	}
	#endregion

	#region /---------Line Mode---------/
	private PenMode _penMode = PenMode.Arbitrary;

	private bool _reversedParabola;

	private bool _isMoving;

	private Point _startPoint;

	private Point _endPoint;

	private StrokeCollection _lastStrokes = new();

	public PenMode PenMode {
		get => _penMode;
		set {
			if (_penMode == value)
				return;
			var oldValue = _penMode;
			SetField(ref _penMode, value);
			if (value == PenMode.Line || oldValue == PenMode.Line)
				OnPropertyChanged(nameof(LineMode));
			if (value == PenMode.Parabola || oldValue == PenMode.Parabola)
				OnPropertyChanged(nameof(ParabolaMode));
			SyncStatus();
		}
	}

	public bool LineMode {
		get => PenMode == PenMode.Line;
		set {
			if (LineMode == value)
				return;
			PenMode = value ? PenMode.Line : PenMode.Arbitrary;
			if (value)
				EraserMode = false;
		}
	}

	public bool ParabolaMode {
		get => PenMode == PenMode.Parabola;
		set {
			if (ParabolaMode == value)
				return;
			PenMode = value ? PenMode.Parabola : PenMode.Arbitrary;
			if (value)
				EraserMode = false;
		}
	}

	private bool ReversedParabola {
		get => _reversedParabola;
		set {
			if (_reversedParabola == value)
				return;
			ParabolaModeButtonIconRotateTransform.Angle = value ? 0 : 180;
			SetField(ref _reversedParabola, value);
		}
	}

	private static Stroke CreateStroke(DrawingAttributes attributes, params Point[] points) => 
		new(new StylusPointCollection(points)) { DrawingAttributes = attributes };

	private void StartLine(object sender, MouseButtonEventArgs e) {
		_isMoving = true;
		_startPoint = e.GetPosition(MainInkCanvas);
		_lastStrokes = new StrokeCollection();
		_ignoreStrokesChange = true;
	}

	private void EndLine(object sender, MouseButtonEventArgs e) {
		if (_isMoving) {
			e.GetPosition(MainInkCanvas);
			if (_lastStrokes.Count > 0)
				_history.Push(new StrokesHistoryNode(_lastStrokes, StrokesHistoryNodeType.Added));
		}
		_isMoving = false;
		_ignoreStrokesChange = false;
	}

	private MouseEventArgs _lastMouseMove;

	private void MakeLine(object sender) => MakeLine(sender, _lastMouseMove);

	private void MakeLine(object sender, MouseEventArgs e) {
		if (!_isMoving)
			return;
		_lastMouseMove = e;
		_endPoint = e.GetPosition(MainInkCanvas);
        var style = MainInkCanvas.DefaultDrawingAttributes.Clone();
		style.StylusTip = StylusTip.Ellipse;
		style.IgnorePressure = true;
		if (_lastStrokes.Count > 0) {
			MainInkCanvas.Strokes.Remove(_lastStrokes);
			_lastStrokes.Clear();
		}
		switch (PenMode) {
			case PenMode.Line: {
				_lastStrokes.Add(CreateStroke(style, _startPoint, _endPoint));
				break;
			}
			case PenMode.Parabola: {
				var o = _startPoint;
				var v = _endPoint - o;
				var k = Settings.Default.ParabolaFactor;
				if (Math.Abs(v.X) < double.Epsilon) {
					var topPoint = v.Y >= 0 ? o : o with { Y = Math.Max(Top, o.Y - v.Y * v.Y / 2 / k) };
					var bottomPoint = o with { Y = Top + Height };
					_lastStrokes.Add(CreateStroke(style, topPoint, bottomPoint));
				}
				else {
					var reversed = ReversedParabola ? -1 : 1;
					var a = k / (2 * v.X * v.X) * reversed;
					var b = v.Y / v.X - k * o.X / (v.X * v.X) * reversed;
					var c = a * o.X * o.X - o.X * v.Y / v.X + o.Y;
					var points = new StylusPointCollection(new[] { _startPoint });
					var delta = (v.X < 0) ^ ReversedParabola ? -Math.PI / 180 : Math.PI / 180;
					for (double theta = Math.Atan(v.Y / v.X) + delta; theta is >= -Math.PI and <= Math.PI; theta += delta) {
						var x = (Math.Tan(theta) - b) / (2 * a);
						if (x < 0 || x > Width)
							break;
						if (points.Count > 1 && Math.Abs(points[^1].X - x) < 1)
							continue;
						var y = (a * x + b) * x + c;
						points.Add(new StylusPoint(x, y));
						if (y < 0 || y > Top + Height)
							break;
					}
					_lastStrokes.Add(new Stroke(points, style));
					_lastStrokes.Add(CreateStroke(style, _startPoint, _endPoint));
				}
				break;
			}
		}
		MainInkCanvas.Strokes.Add(_lastStrokes);
	}

	private void AdjustParabolaFactor(int delta) {
		var n = _endPoint.Y - _startPoint.Y;
		var peak = n * n / 2 / Settings.Default.ParabolaFactor;
		if (peak * delta < 0 && Math.Abs(peak) <= Math.Abs(delta))
			return;
		Settings.Default.ParabolaFactor = n * n / 2 / (peak + delta);
		Settings.Default.Save();
	}
	#endregion
}