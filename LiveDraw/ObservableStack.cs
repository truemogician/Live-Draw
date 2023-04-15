using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AntFu7.LiveDraw;

public class ObservableStack<T> : Stack<T>, INotifyCollectionChanged, INotifyPropertyChanged {
	public ObservableStack() { }

	public ObservableStack(IEnumerable<T> collection) {
		foreach (var item in collection)
			base.Push(item);
	}

	public ObservableStack(List<T> list) {
		foreach (var item in list)
			base.Push(item);
	}

	public new virtual void Clear() {
		base.Clear();
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
	}

	public new virtual T Pop() {
		var item = base.Pop();
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
		return item;
	}

	public new virtual void Push(T item) {
		base.Push(item);
		OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item));
	}

	public virtual event NotifyCollectionChangedEventHandler CollectionChanged;

	protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e) 
		=> CollectionChanged?.Invoke(this, e);

	protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) 
		=> PropertyChanged?.Invoke(this, e);

	protected event PropertyChangedEventHandler PropertyChanged;

	event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged {
		add => PropertyChanged += value;
		remove => PropertyChanged -= value;
	}
}