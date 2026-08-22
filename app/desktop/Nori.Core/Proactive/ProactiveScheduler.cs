using System.Globalization;
using Nori.Core.Configuration;
using Nori.Core.Logging;
using Nori.Core.Tools;

namespace Nori.Core.Proactive;

/// <summary>
/// 主动交互与定时提醒调度器
///
/// 职责:
/// - 持久化提醒的到期触发 (重启后自动恢复)
/// - 日常时段问候 (早安 / 午餐 / 晚安, 可配置开关)
/// - 挂机检测触发主动关怀 (依赖平台空闲时长, 不可用时静默跳过)
///
/// 触发的台词通过 ProactiveMessage 事件交给宿主: 由桌宠播放动作/表情,
/// 开启自动朗读时由语音服务朗读。
/// </summary>
public sealed class ProactiveScheduler : IDisposable
{
	/// <summary>默认挂机阈值 (分钟)</summary>
	public const int DefaultIdleMinutes = 15;

	private readonly ReminderStore _store;
	private readonly ConfigStore _config;
	private readonly FileLogger _logger;
	private readonly Func<double?> _idleSecondsProvider;

	private readonly Lock _gate = new();
	private System.Threading.Timer? _tickTimer;

	/// <summary>当天已触发的问候 (日期-槽位)</summary>
	private readonly HashSet<string> _firedDailyGreetings = [];
	/// <summary>上次挂机触发时间戳, 防止频繁刷屏</summary>
	private long _lastIdleFiredAt;

	/// <summary>主动发声事件</summary>
	public event Action<ProactiveMessage>? Message;

	public ProactiveScheduler(ReminderStore store, ConfigStore config, FileLogger logger, Func<double?> idleSecondsProvider)
	{
		_store = store;
		_config = config;
		_logger = logger;
		_idleSecondsProvider = idleSecondsProvider;
	}

	/// <summary>启动调度循环 (30s 粒度)</summary>
	public void Start()
	{
		lock (_gate)
		{
			_tickTimer ??= new System.Threading.Timer(_ => SafeTick(), null, 5000, 30_000);
		}
	}

	/// <summary>设置一个提醒 (如 30 分钟后提醒喝水)</summary>
	public ReminderItem AddReminder(string content, double delayMinutes)
	{
		if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("提醒内容不能为空");
		if (delayMinutes <= 0 || delayMinutes > 60 * 24 * 30) throw new InvalidOperationException("提醒延迟超出范围");
		long triggerAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(delayMinutes * 60_000);
		return _store.Add(content.Trim(), triggerAt);
	}

	/// <summary>列出所有排队中的提醒</summary>
	public IReadOnlyList<ReminderItem> ListReminders() => _store.List();

	/// <summary>取消提醒</summary>
	public bool CancelReminder(string id) => _store.Delete(id);

	/// <summary>手动触发一次主动发言 (供调试)</summary>
	public void SayNow(string text, string motion = "wave", string expression = "Smile") =>
		Message?.Invoke(new ProactiveMessage(text, motion, expression));

	private void SafeTick()
	{
		try
		{
			Tick();
		}
		catch (Exception exception)
		{
			try
			{
				_logger.Write(LogSource.Backend, "warn", $"主动交互调度异常: {exception.Message}");
			}
			catch
			{
				// 日志失败保持静默
			}
		}
	}

	private void Tick()
	{
		long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		FireDueReminders(nowMs);
		CheckDailyGreetings();
		CheckIdle();
	}

	private void FireDueReminders(long nowMs)
	{
		foreach (ReminderItem reminder in _store.TakeDue(nowMs))
		{
			string text = $"主人！提醒时间到了：{reminder.Content}";
			Message?.Invoke(new ProactiveMessage(text, "wave", "Surprised"));
			try
			{
				_logger.Write(LogSource.Backend, "info", $"定时提醒触发: {reminder.Content}");
			}
			catch
			{
				// 忽略日志失败
			}
		}
	}

	private void CheckDailyGreetings()
	{
		bool enabled = ParseBool(_config.GetStringOr("proactive_daily_greeting", "true")) ?? true;
		if (!enabled) return;

		DateTime now = DateTime.Now;
		string dateKey = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		int hour = now.Hour;
		int minute = now.Minute;

		void FireOnce(string slot, string text, string motion, string expression)
		{
			lock (_gate)
			{
				if (!_firedDailyGreetings.Add($"{dateKey}-{slot}")) return;
			}
			Message?.Invoke(new ProactiveMessage(text, motion, expression));
		}

		// 8:30 晨间问候
		if (hour == 8 && minute >= 30)
		{
			FireOnce("morning", "早安主人！新的一天也要元气满满哦~", "wave", "Smile");
		}
		// 12:00 午餐提醒
		else if (hour == 12 && minute <= 15)
		{
			FireOnce("lunch", "到午饭时间啦！不要饿肚子，去吃点好吃的吧~", "smile", "Smile");
		}
		// 23:00 晚安提醒
		else if (hour == 23 && minute is >= 0 and <= 15)
		{
			FireOnce("night", "夜深了，工作再忙也要注意身体，早点休息吧主人~", "think", "Sleepy");
		}
	}

	private void CheckIdle()
	{
		bool enabled = ParseBool(_config.GetStringOr("proactive_idle_enabled", "true")) ?? true;
		if (!enabled) return;
		double thresholdSeconds = Math.Max(1,
			ParseDouble(_config.GetStringOr("proactive_idle_minutes", DefaultIdleMinutes.ToString(CultureInfo.InvariantCulture)), DefaultIdleMinutes) * 60);

		double? idleSeconds = _idleSecondsProvider();
		if (idleSeconds is not { } idle || idle < thresholdSeconds) return;

		long now = Environment.TickCount64;
		// 同一轮挂机只关怀一次: 触发后至少再等一个完整阈值周期
		if (now - Interlocked.Read(ref _lastIdleFiredAt) < (long)(thresholdSeconds * 1000)) return;
		Interlocked.Exchange(ref _lastIdleFiredAt, now);

		string[] texts =
		[
			"主人已经好久没有理 Nori 啦...",
			"伸个懒腰~ 工作辛苦啦，记得休息一下眼睛哦！",
			"呼啊... 好困呀，主人在忙什么呢？",
		];
		string[] motions = ["think", "smile", "wave"];
		string[] expressions = ["Sad", "Smile", "Sleepy"];
		int pick = Random.Shared.Next(texts.Length);
		Message?.Invoke(new ProactiveMessage(texts[pick], motions[pick], expressions[pick]));
	}

	private static bool? ParseBool(string raw) => raw switch
	{
		"1" => true,
		"0" => false,
		_ when raw.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
		_ when raw.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
		_ => null,
	};

	private static double ParseDouble(string raw, double fallback) =>
		double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;

	public void Dispose()
	{
		System.Threading.Timer? timer;
		lock (_gate)
		{
			timer = _tickTimer;
			_tickTimer = null;
		}
		timer?.Dispose();
	}
}

/// <summary>主动发声消息</summary>
public sealed record ProactiveMessage(string Text, string Motion, string Expression);
