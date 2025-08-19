using System;
using System.Diagnostics;
using System.Windows.Threading;

public class SeekManager
{
    // 内部状态变量
    private readonly DispatcherTimer _cooldownTimer;
    private long _lastExecutionTimestamp = 0;
    private bool _isActivelySeeking = false;
    private bool _wasMutedBeforeSeek = false;
    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    // 配置常量
    private readonly long _throttleIntervalTicks;
    private readonly long _cooldownIntervalTicks;

    // 与外部（ViewerPanel）交互的委托 (Delegates)
    private readonly Action<int> _executeSeekAction;
    private readonly Action<bool> _setMuteAction;
    private readonly Func<bool> _getMuteAction;
    
    
    private readonly Stopwatch _seekSessionStopwatch = new Stopwatch(); // 用于记录单次连续寻道的时长
    private long _mediaDurationTicks = 0; // 用于存储媒体总时长
    /// <summary>
    /// 初始化寻道管理器
    /// </summary>
    /// <param name="executeSeekAction">一个用于执行实际寻道操作的委托</param>
    /// <param name="setMuteAction">设置播放器静音的委托</param>
    /// <param name="getMuteAction">获取播放器当前静音状态的委托</param>
    public SeekManager(Action<int> executeSeekAction, Action<bool> setMuteAction, Func<bool> getMuteAction)
    {
        _executeSeekAction = executeSeekAction;
        _setMuteAction = setMuteAction;
        _getMuteAction = getMuteAction;
        
        // 可配置的间隔
        _throttleIntervalTicks = TimeSpan.FromMilliseconds(150).Ticks;
        _cooldownIntervalTicks = TimeSpan.FromMilliseconds(300).Ticks;

        _cooldownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromTicks(_cooldownIntervalTicks)
        };
        _cooldownTimer.Tick += OnSeekCooldown;
    }
    /// <summary>
    ///  设置媒体总时长，用于计算最大步长
    /// </summary>
    public void SetMediaDuration(long durationTicks)
    {
        _mediaDurationTicks = durationTicks;
    }
    
    /// <summary>
    /// [新增] 根据寻道会话时长计算步长乘数 (Ease-In 曲线)
    /// </summary>
    private double CalculateStepMultiplier()
    {
        // 使用简单的二次方函数实现 Ease-In 加速效果
        var sessionSeconds = _seekSessionStopwatch.Elapsed.TotalSeconds;
        return 1.0 + (sessionSeconds * sessionSeconds * 0.5);
    }

    /// <summary>
    /// 外部调用的唯一入口：请求一次寻道操作
    /// </summary>
    public void RequestSeek(int seconds)
    {
        if (!_isActivelySeeking)
        {
            // 开始一次新的连续寻道会话
            _isActivelySeeking = true;
            _wasMutedBeforeSeek = _getMuteAction();
            _setMuteAction(true);
            _seekSessionStopwatch.Restart(); // 启动或重置会话计时器
        }

        long now = _stopwatch.Elapsed.Ticks;
        if (now - _lastExecutionTimestamp < _throttleIntervalTicks)
        {
            // 节流：如果触发过于频繁，则仅重置冷却计时器而不执行寻道
            _cooldownTimer.Stop();
            _cooldownTimer.Start();
            return;
        }

        _lastExecutionTimestamp = now;
        
        // --- 核心修改逻辑 ---

        // 1. 计算动态步长乘数
        double multiplier = CalculateStepMultiplier();
        double acceleratedStep = seconds * multiplier;

        // 2. 计算允许的最大步长 (视频总时长的10%)
        double maxStepSeconds = _mediaDurationTicks > 0
            ? (_mediaDurationTicks / (double)TimeSpan.TicksPerSecond) * 0.10
            : 30.0; // 如果没有时长信息

        // 3. 限制步长不超过最大值
        if (Math.Abs(acceleratedStep) > maxStepSeconds)
        {
            acceleratedStep = Math.Sign(acceleratedStep) * maxStepSeconds;
        }
        
        // --- 逻辑结束 ---

        // 调用通过委托传入的实际寻道方法，使用计算出的新步长
        _executeSeekAction((int)Math.Round(acceleratedStep));

        _cooldownTimer.Stop();
        _cooldownTimer.Start();
    }
    private void OnSeekCooldown(object sender, EventArgs e)
    {
        _cooldownTimer.Stop();
        if (_isActivelySeeking)
        {
            // 结束连续寻道会话
            _isActivelySeeking = false;
            _seekSessionStopwatch.Stop(); // 停止会话计时
            _setMuteAction(_wasMutedBeforeSeek); // 恢复寻道前的静音状态
        }
    }

}