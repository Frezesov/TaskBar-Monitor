using TaskbarMonitor.Core;

namespace TaskbarMonitor.ViewModels;

public sealed class MetricItemViewModel : ObservableObject
{
    private readonly MetricEntry _entry;
    private readonly Action _changed;

    internal MetricItemViewModel(MetricEntry entry, Action changed, Action<MetricItemViewModel, int> move)
    {
        _entry = entry;
        _changed = changed;
        MoveUpCommand = new RelayCommand(() => move(this, -1));
        MoveDownCommand = new RelayCommand(() => move(this, +1));
    }

    public MetricKind Kind => _entry.Kind;

    public bool Enabled
    {
        get => _entry.Enabled;
        set
        {
            if (_entry.Enabled == value)
                return;
            _entry.Enabled = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public bool CanMoveUp
    {
        get;
        internal set => Set(ref field, value);
    }

    public bool CanMoveDown
    {
        get;
        internal set => Set(ref field, value);
    }

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    public string Title => Kind switch
    {
        MetricKind.Cpu => "Процессор",
        MetricKind.Ram => "Оперативная память",
        MetricKind.Gpu => "Видеокарта",
        MetricKind.GpuTemp => "Температура видеокарты",
        MetricKind.Vram => "Видеопамять",
        MetricKind.NetUp => "Сеть: отправка",
        MetricKind.NetDown => "Сеть: загрузка",
        MetricKind.DiskActivity => "Активность дисков",
        MetricKind.DiskSpace => "Заполненность диска",
        _ => Kind.ToString(),
    };

    public string Description => Kind switch
    {
        MetricKind.Cpu => "Загрузка всех ядер — так же, как в диспетчере задач",
        MetricKind.Ram => "Сколько оперативной памяти занято",
        MetricKind.Gpu => "Самый загруженный блок GPU: 3D, видео или вычисления",
        MetricKind.GpuTemp => "Температура графического процессора в °C",
        MetricKind.Vram => "Сколько выделенной видеопамяти занято",
        MetricKind.NetUp => "Скорость исходящего трафика",
        MetricKind.NetDown => "Скорость входящего трафика",
        MetricKind.DiskActivity => "Время активности самого загруженного диска",
        MetricKind.DiskSpace => "Сколько места занято на выбранном диске",
        _ => "",
    };

    public string Glyph => Kind switch
    {
        MetricKind.Cpu => "",
        MetricKind.Ram => "",
        MetricKind.Gpu => "",
        MetricKind.GpuTemp => "",
        MetricKind.Vram => "",
        MetricKind.NetUp => "",
        MetricKind.NetDown => "",
        MetricKind.DiskActivity => "",
        MetricKind.DiskSpace => "",
        _ => "",
    };
}
