using System;
using System.Collections.Generic;
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
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Screen = System.Windows.Forms.Screen;

namespace AntFu7.LiveDraw; 

public partial class MainWindow: INotifyPropertyChanged {
	private static int EraseByPointFlag;

	public enum EraseMode {
		None = 0,

		Eraser = 1,

		EraserByPoint = 2
	}

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
		_history = new Stack<StrokesHistoryNode>();
		_redoHistory = new Stack<StrokesHistoryNode>();
		if (!Directory.Exists("Save"))
			Directory.CreateDirectory("Save");

		InitializeComponent();
		Color = DefaultColorPicker;
		Enable = false;
		ShowDetailedPanel = true;
		TopMost = true;
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

	private bool _isInkVisible = true;

	private bool _showDetailedPanel;

	private bool _eraserMode;

	private bool _enable;

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

	private bool IsInkVisible {
		get => _isInkVisible;
		set {
			MainInkCanvas.BeginAnimation(
				OpacityProperty,
				value ? new DoubleAnimation(0, 1, Duration3) : new DoubleAnimation(1, 0, Duration3)
			);
			HideButton.IsActivated = !value;
			Enable = value;
			_isInkVisible = value;
		}
	}

	private bool Enable {
		get => _enable;
		set {
			EnableButton.IsActivated = !value;
			Background = Application.Current.Resources[value ? "FakeTransparent" : "TrueTransparent"] as Brush;
			_enable = value;
			MainInkCanvas.UseCustomCursor = false;

			if (_enable) {
				LineButton.IsActivated = false;
				EraserButton.IsActivated = false;
				SetStaticInfo("LiveDraw");
				MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
			}
			else {
				SetStaticInfo("Locked");
				MainInkCanvas.EditingMode = InkCanvasEditingMode.None;//No inking possible
			}
		}
	}

	private ColorPicker Color {
		get => _color;
		set {
			if (ReferenceEquals(_color, value))
				return;
			if (value.Background is not SolidColorBrush solidColorBrush)
				return;

			var ani = new ColorAnimation(solidColorBrush.Color, Duration3);

			MainInkCanvas.DefaultDrawingAttributes.Color = solidColorBrush.Color;
			BrushPreview.Background.BeginAnimation(SolidColorBrush.ColorProperty, ani);
			value.IsActivated = true;
			if (_color != null)
				_color.IsActivated = false;
			_color = value;
		}
	}

	private bool EraserMode {
		get => _eraserMode;
		set {
			EraserButton.IsActivated = value;
			_eraserMode = value;
			MainInkCanvas.UseCustomCursor = false;

			if (_eraserMode) {
				MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
				SetStaticInfo("Eraser Mode");
			}
			else
				Enable = _enable;
		}
	}

	private bool UseVerticalDisplay {
		get => _useVerticalDisplay;
		set {
			PaletteRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(value ? -90 : 0, Duration4));
			Palette.BeginAnimation(MinWidthProperty, new DoubleAnimation(value ? 90 : 0, Duration7));
			_useVerticalDisplay = value;
		}
	}

	private bool TopMost {
		get => Topmost;
		set {
			Topmost = value;
			PinButton.IsActivated = value;
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

	#region /---------Generator---------/
	private static string GenerateFileName(string fileExt = ".fdw") => DateTime.Now.ToString("yyyyMMdd-HHmmss") + fileExt;
	#endregion

	#region /---------Helper---------/
	private string _staticInfo = "";

	private bool _displayingInfo;

	private async void Display(string info) {
		InfoBox.Text = info;
		_displayingInfo = true;
		await InfoDisplayTimeUp(new Progress<string>(box => InfoBox.Text = box));
	}

	private Task InfoDisplayTimeUp(IProgress<string> box) {
		return Task.Run(
			() => {
				Task.Delay(2000).Wait();
				box.Report(_staticInfo);
				_displayingInfo = false;
			}
		);
	}

	private void SetStaticInfo(string info) {
		_staticInfo = info;
		if (!_displayingInfo)
			InfoBox.Text = _staticInfo;
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
		var dialog = new Microsoft.Win32.OpenFileDialog() {
			DefaultExt = fileExt,
			Filter = filter
		};
		return dialog.ShowDialog() == true ? dialog.OpenFile() : Stream.Null;
	}

	private void EraserFunction() {
		LineMode = false;
		switch (EraseByPointFlag) {
			case (int)EraseMode.None:
				EraserMode = !EraserMode;
				EraserButton.ToolTip = "Toggle eraser (by point) mode (D)";
				EraseByPointFlag = (int)EraseMode.Eraser;
				break;
			case (int)EraseMode.Eraser: {
				EraserButton.IsActivated = true;
				SetStaticInfo("Eraser Mode (Point)");
				EraserButton.ToolTip = "Toggle eraser - OFF";
				double s = MainInkCanvas.EraserShape.Height;
				MainInkCanvas.EraserShape = new EllipseStylusShape(s, s);
				MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
				EraseByPointFlag = (int)EraseMode.EraserByPoint;
				break;
			}
			case (int)EraseMode.EraserByPoint:
				EraserMode = !EraserMode;
				EraserButton.ToolTip = "Toggle eraser mode (E)";
				EraseByPointFlag = (int)EraseMode.None;
				break;
		}
	}

	private void AdjustWindowSize() {
		var primaryArea = Screen.PrimaryScreen!.WorkingArea;
		var scaleRatio = Math.Max(
			primaryArea.Width / SystemParameters.PrimaryScreenWidth,
			primaryArea.Height / SystemParameters.PrimaryScreenHeight
		);
		Left = Screen.AllScreens.Min(s => s.WorkingArea.Left) / scaleRatio;
		Top = Screen.AllScreens.Min(s => s.WorkingArea.Top) / scaleRatio;
		Width = Screen.AllScreens.Max(s => s.WorkingArea.Right) / scaleRatio - Left;
		Height = Screen.AllScreens.Max(s => s.WorkingArea.Bottom) / scaleRatio - Top;
			
		Canvas.SetTop(Palette, (primaryArea.Top + primaryArea.Height / 2.0) / scaleRatio - Top - Palette.ActualHeight / 2);
		Canvas.SetLeft(Palette, (primaryArea.Left + primaryArea.Width / 2.0) / scaleRatio - Left - Palette.ActualWidth / 2);
	}
	#endregion

	#region /---------Ink---------/
	private readonly Stack<StrokesHistoryNode> _history;

	private readonly Stack<StrokesHistoryNode> _redoHistory;

	private bool _ignoreStrokesChange;

	private void Undo() {
		if (!CanUndo())
			return;
		var last = Pop(_history);
		_ignoreStrokesChange = true;
		if (last.Type == StrokesHistoryNodeType.Added)
			MainInkCanvas.Strokes.Remove(last.Strokes);
		else
			MainInkCanvas.Strokes.Add(last.Strokes);
		_ignoreStrokesChange = false;
		Push(_redoHistory, last);
	}

	private void Redo() {
		if (!CanRedo())
			return;
		var last = Pop(_redoHistory);
		_ignoreStrokesChange = true;
		if (last.Type == StrokesHistoryNodeType.Removed)
			MainInkCanvas.Strokes.Remove(last.Strokes);
		else
			MainInkCanvas.Strokes.Add(last.Strokes);
		_ignoreStrokesChange = false;
		Push(_history, last);
	}

	private static void Push(Stack<StrokesHistoryNode> collection, StrokesHistoryNode node) => collection.Push(node);

	private static StrokesHistoryNode Pop(Stack<StrokesHistoryNode> collection) => collection.Count == 0 ? null : collection.Pop();

	private bool CanUndo() => _history.Count != 0;

	private bool CanRedo() => _redoHistory.Count != 0;

	private void StrokesChanged(object sender, StrokeCollectionChangedEventArgs e) {
		if (_ignoreStrokesChange)
			return;
		_saved = false;
		if (e.Added.Count != 0)
			Push(_history, new StrokesHistoryNode(e.Added, StrokesHistoryNodeType.Added));
		if (e.Removed.Count != 0)
			Push(_history, new StrokesHistoryNode(e.Removed, StrokesHistoryNodeType.Removed));
		ClearHistory(_redoHistory);
	}

	private void ClearHistory() {
		ClearHistory(_history);
		ClearHistory(_redoHistory);
	}

	private static void ClearHistory(Stack<StrokesHistoryNode> collection) => collection?.Clear();

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

		if (EraseByPointFlag != (int)EraseMode.None) {
			EraserMode = false;
			EraseByPointFlag = (int)EraseMode.None;
			EraserButton.ToolTip = "Toggle eraser mode (E)";
		}
	}

	private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
		//SetBrushSize(e.NewValue);
	}

	private void MainInkCanvas_MouseWheel(object sender, MouseWheelEventArgs e) {
		if (e.Delta < 0)
			--BrushIndex;
		else
			++BrushIndex;
	}

	private void BrushSwitchButton_Click(object sender, RoutedEventArgs e) => ++BrushIndex;

	private void LineButton_Click(object sender, RoutedEventArgs e) => LineMode = !LineMode;

	private void UndoButton_Click(object sender, RoutedEventArgs e) => Undo();

	private void RedoButton_Click(object sender, RoutedEventArgs e) => Redo();

	private void EraserButton_Click(object sender, RoutedEventArgs e) {
		if (Enable)
			EraserFunction();
	}

	private void ClearButton_Click(object sender, RoutedEventArgs e) => AnimatedClear();

	private void PinButton_Click(object sender, RoutedEventArgs e) => TopMost = !TopMost;

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

	private void HideButton_Click(object sender, RoutedEventArgs e) => IsInkVisible = !IsInkVisible;

	private void EnableButton_Click(object sender, RoutedEventArgs e) {
		Enable = !Enable;
		if (_eraserMode) {
			EraserMode = !EraserMode;
			EraserButton.ToolTip = "Toggle eraser mode (E)";
			EraseByPointFlag = (int)EraseMode.None;
		}
	}

	private void OrientationButton_Click(object sender, RoutedEventArgs e) => UseVerticalDisplay = !UseVerticalDisplay;
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
		_tempEnable = Enable;
		Enable = true;
	}

	private void EndDrag() {
		if (_isDragging)
			Enable = _tempEnable;
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
			Enable = !Enable;
		if (!Enable)
			return;

		switch (e.Key) {
			case Key.Z:
				Undo();
				break;
			case Key.Y:
				Redo();
				break;
			case Key.E:
				EraserFunction();
				break;
			case Key.B:
				if (_eraserMode)
					EraserMode = false;
				Enable = true;
				break;
			case Key.L:
				if (_eraserMode)
					EraserMode = false;
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
	private bool _isMoving;

	private bool _lineMode;

	private Point _startPoint;

	private Stroke _lastStroke;

	private bool LineMode {
		get => _lineMode;
		set {
			if (!Enable)
				return;
			_lineMode = value;
			if (_lineMode) {
				EraseByPointFlag = (int)EraseMode.EraserByPoint;
				EraserFunction();
				EraserMode = false;
				EraserButton.IsActivated = false;
				LineButton.IsActivated = value;
				SetStaticInfo("LineMode");
				MainInkCanvas.EditingMode = InkCanvasEditingMode.None;
				MainInkCanvas.UseCustomCursor = true;
			}
			else
				Enable = true;
		}
	}

	private void StartLine(object sender, MouseButtonEventArgs e) {
		_isMoving = true;
		_startPoint = e.GetPosition(MainInkCanvas);
		_lastStroke = null;
		_ignoreStrokesChange = true;
	}

	private void EndLine(object sender, MouseButtonEventArgs e) {
		if (_isMoving) {
			e.GetPosition(MainInkCanvas);
			if (_lastStroke != null) {
				var collection = new StrokeCollection { _lastStroke };
				Push(_history, new StrokesHistoryNode(collection, StrokesHistoryNodeType.Added));
			}
		}
		_isMoving = false;
		_ignoreStrokesChange = false;
	}

	private void MakeLine(object sender, MouseEventArgs e) {
		if (_isMoving == false)
			return;

		var newLine = MainInkCanvas.DefaultDrawingAttributes.Clone();
		newLine.StylusTip = StylusTip.Ellipse;
		newLine.IgnorePressure = true;

		var endPoint = e.GetPosition(MainInkCanvas);

		var pList = new List<Point> {
			new(_startPoint.X, _startPoint.Y),
			new(endPoint.X, endPoint.Y)
		};

		var point = new StylusPointCollection(pList);
		var stroke = new Stroke(point) { DrawingAttributes = newLine };

		if (_lastStroke != null)
			MainInkCanvas.Strokes.Remove(_lastStroke);
		MainInkCanvas.Strokes.Add(stroke);

		_lastStroke = stroke;
	}
	#endregion
}