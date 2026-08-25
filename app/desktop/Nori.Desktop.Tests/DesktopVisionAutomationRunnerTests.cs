using System.Text.Json;
using Nori.Core.Automation;
using Nori.Core.Chat;
using Nori.Desktop.Automation.Desktop;

namespace Nori.Desktop.Tests;

/// <summary>独立桌面视觉执行器的边界和脱敏行为测试。</summary>
public sealed class DesktopVisionAutomationRunnerTests
{
	[Fact]
	public async Task 成功执行一个动作并接受结构化完成结果()
	{
		FakeScreenshotSource screenshots = new();
		FakePlanner planner = new();
		planner.Responses.Enqueue("{\"type\":\"click\",\"x\":10,\"y\":20}");
		planner.Responses.Enqueue("{\"status\":\"completed\"}");
		FakeActionExecutor executor = new();
		List<DesktopVisionProgress> progress = [];
		DesktopVisionAutomationRunner runner = CreateRunner(screenshots, executor, planner, progress: progress.Add);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());

		Assert.True(result.Succeeded);
		Assert.Equal(DesktopVisionAutomationCategory.Completed, result.Category);
		Assert.Single(executor.Actions);
		Assert.Equal(DesktopVisionAutomationCategory.Completed, progress[^1].Category);
		Assert.DoesNotContain(progress, item => item.Category == DesktopVisionAutomationCategory.InvalidAction);
	}

	[Theory]
	[InlineData("```json\n{\"type\":\"click\",\"x\":10,\"y\":20}\n```")]
	[InlineData("{not-json")]
	[InlineData("[{\"type\":\"click\",\"x\":10,\"y\":20}]")]
	[InlineData("{\"type\":\"click\",\"x\":10,\"y\":20,\"extra\":true}")]
	public async Task 拒绝Markdown数组和额外字段(string modelText)
	{
		FakePlanner planner = new(modelText);
		FakeActionExecutor executor = new();
		DesktopVisionAutomationRunner runner = CreateRunner(new FakeScreenshotSource(), executor, planner);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());

		Assert.False(result.Succeeded);
		Assert.Equal(DesktopVisionAutomationCategory.InvalidAction, result.Category);
		Assert.Empty(executor.Actions);
	}

	[Fact]
	public async Task 策略越界在执行前返回稳定类别()
	{
		FakePlanner planner = new("{\"type\":\"click\",\"x\":1920,\"y\":20}");
		FakeActionExecutor executor = new();
		DesktopVisionAutomationRunner runner = CreateRunner(new FakeScreenshotSource(), executor, planner);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());

		Assert.Equal(DesktopVisionAutomationCategory.PolicyRejected, result.Category);
		Assert.Empty(executor.Actions);
	}

	[Fact]
	public async Task 高风险文本输入被拒绝时不执行动作()
	{
		FakePlanner planner = new("{\"type\":\"type_text\",\"text\":\"不应出现在状态\"}");
		FakeActionExecutor executor = new();
		List<AutomationApprovalRequest> requests = [];
		DesktopVisionApprovalCallback approval = (request, _) =>
		{
			requests.Add(request);
			return Task.FromResult(AutomationApprovalDecision.Create(request, AutomationApprovalOutcome.Denied, DateTimeOffset.UtcNow));
		};
		DesktopVisionAutomationRunner runner = CreateRunner(new FakeScreenshotSource(), executor, planner, approval);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());

		Assert.Equal(DesktopVisionAutomationCategory.ApprovalDenied, result.Category);
		Assert.Empty(executor.Actions);
		Assert.Single(requests);
		Assert.Equal([AutomationActionKind.TypeText], requests[0].ActionKinds);
	}

	[Fact]
	public async Task 取消会中断不响应取消的规划器()
	{
		FakePlanner planner = new();
		TaskCompletionSource<bool> started = Signal<bool>();
		TaskCompletionSource<string> release = Signal<string>();
		planner.Handler = (_, _) =>
		{
			started.TrySetResult(true);
			return release.Task;
		};
		DesktopVisionAutomationRunner runner = CreateRunner(new FakeScreenshotSource(), new FakeActionExecutor(), planner);
		using CancellationTokenSource cancellation = new();
		Task<DesktopVisionAutomationResult> running = runner.ExecuteAsync(Context(), cancellation.Token);

		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();
		DesktopVisionAutomationResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		release.TrySetResult("{\"status\":\"completed\"}");

		Assert.Equal(DesktopVisionAutomationCategory.Cancelled, result.Category);
	}

	[Fact]
	public async Task 超时会中断不响应取消的规划器()
	{
		FakePlanner planner = new();
		TaskCompletionSource<bool> started = Signal<bool>();
		TaskCompletionSource<string> release = Signal<string>();
		planner.Handler = (_, _) =>
		{
			started.TrySetResult(true);
			return release.Task;
		};
		DesktopVisionAutomationRunner runner = CreateRunner(
			new FakeScreenshotSource(),
			new FakeActionExecutor(),
			planner,
			options: new DesktopVisionAutomationOptions {Timeout = TimeSpan.FromMilliseconds(50)});

		Task<DesktopVisionAutomationResult> running = runner.ExecuteAsync(Context());
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		DesktopVisionAutomationResult result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		release.TrySetResult("{\"status\":\"completed\"}");

		Assert.Equal(DesktopVisionAutomationCategory.Timeout, result.Category);
	}

	[Fact]
	public async Task 最多执行二十步后停止继续规划()
	{
		FakePlanner planner = new();
		planner.Handler = (_, _) => Task.FromResult("{\"type\":\"click\",\"x\":10,\"y\":20}");
		FakeActionExecutor executor = new();
		DesktopVisionAutomationRunner runner = CreateRunner(new FakeScreenshotSource(), executor, planner);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());

		Assert.Equal(DesktopVisionAutomationCategory.StepLimitExceeded, result.Category);
		Assert.Equal(DesktopVisionAutomationOptions.MaximumSteps, executor.Actions.Count);
		Assert.Equal(DesktopVisionAutomationOptions.MaximumSteps, planner.CallCount);
	}

	[Fact]
	public async Task 图片单部分界限允许精确上限并拒绝超限()
	{
		FakePlanner acceptedPlanner = new("{\"status\":\"completed\"}");
		FakeScreenshotSource acceptedScreenshots = new(new byte[ChatImagePart.MaxBytes]);
		DesktopVisionAutomationRunner acceptedRunner = CreateRunner(acceptedScreenshots, new FakeActionExecutor(), acceptedPlanner);

		DesktopVisionAutomationResult accepted = await acceptedRunner.ExecuteAsync(Context());

		Assert.Equal(DesktopVisionAutomationCategory.Completed, accepted.Category);
		Assert.Equal(ChatImagePart.MaxBytes, acceptedPlanner.Messages[0][0].ImageParts[0].Bytes.Length);

		FakePlanner rejectedPlanner = new("{\"status\":\"completed\"}");
		FakeScreenshotSource rejectedScreenshots = new(new byte[ChatImagePart.MaxBytes + 1]);
		DesktopVisionAutomationRunner rejectedRunner = CreateRunner(rejectedScreenshots, new FakeActionExecutor(), rejectedPlanner);

		DesktopVisionAutomationResult rejected = await rejectedRunner.ExecuteAsync(Context());

		Assert.Equal(DesktopVisionAutomationCategory.ScreenshotFailed, rejected.Category);
		Assert.Empty(rejectedPlanner.Messages);
	}

	[Fact]
	public void 图片总大小精确上限允许而超过上限拒绝()
	{
		ChatImagePart first = new(new byte[ChatImagePart.MaxBytes], "image/png");
		ChatImagePart second = new(new byte[ChatImagePart.MaxBytes], "image/png");
		ChatImagePart extra = new([1], "image/png");

		ChatMessageInput accepted = new()
		{
			Role = "user",
			Content = "脱敏",
			ImageParts = [first, second],
		};
		Assert.Equal(ChatMessageInput.MaxTotalImageBytes, accepted.ImageParts.Sum(item => item.Bytes.Length));

		Assert.Throws<ChatException>(() => new ChatMessageInput
		{
			Role = "user",
			Content = "脱敏",
			ImageParts = [first, second, extra],
		});
	}

	[Fact]
	public async Task 目标失去前台不会执行动作且进度不含敏感正文()
	{
		FakePlanner planner = new("{\"type\":\"click\",\"x\":10,\"y\":20}");
		FakeScreenshotSource screenshots = new(DesktopVisionScreenshotResult.TargetNotForeground);
		FakeActionExecutor executor = new();
		List<DesktopVisionProgress> progress = [];
		DesktopVisionAutomationRunner runner = CreateRunner(screenshots, executor, planner, progress: progress.Add);

		DesktopVisionAutomationResult result = await runner.ExecuteAsync(Context());
		string state = JsonSerializer.Serialize(new {result, progress});

		Assert.Equal(DesktopVisionAutomationCategory.TargetNotForeground, result.Category);
		Assert.Empty(executor.Actions);
		Assert.DoesNotContain("不应出现在状态", state, StringComparison.Ordinal);
		Assert.DoesNotContain("type_text", state, StringComparison.Ordinal);
	}

	private static DesktopVisionAutomationRunner CreateRunner(
		FakeScreenshotSource screenshots,
		FakeActionExecutor executor,
		FakePlanner planner,
		DesktopVisionApprovalCallback? approval = null,
		DesktopVisionAutomationOptions? options = null,
		Action<DesktopVisionProgress>? progress = null) =>
		new(
			"脱敏标题",
			"脱敏目标",
			new nint(42),
			screenshots,
			executor,
			planner,
			approval,
			options: options,
			progress: progress);

	private static AutomationTaskContext Context() => new(Guid.NewGuid());

	private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	private sealed class FakeScreenshotSource : IDesktopVisionScreenshotSource
	{
		private readonly Queue<DesktopVisionScreenshotResult> _results = [];

		public FakeScreenshotSource()
		{
			_results.Enqueue(DesktopVisionScreenshotResult.Succeeded(new DesktopVisionScreenshot([1, 2, 3], "image/png")));
		}

		public FakeScreenshotSource(byte[] data)
		{
			_results.Enqueue(DesktopVisionScreenshotResult.Succeeded(new DesktopVisionScreenshot(data, "image/png")));
		}

		public FakeScreenshotSource(DesktopVisionScreenshotResult result)
		{
			_results.Enqueue(result);
		}

		public Task<DesktopVisionScreenshotResult> CaptureAsync(nint targetWindow, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			DesktopVisionScreenshotResult result = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
			return Task.FromResult(result);
		}
	}

	private sealed class FakeActionExecutor : IDesktopVisionActionExecutor
	{
		public List<AutomationAction> Actions { get; } = [];
		public DesktopVisionActionResult Result { get; set; } = DesktopVisionActionResult.Succeeded;

		public Task<DesktopVisionActionResult> ExecuteAsync(
			nint targetWindow,
			AutomationAction action,
			AutomationPolicy policy,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Actions.Add(action);
			return Task.FromResult(Result);
		}
	}

	private sealed class FakePlanner : IDesktopVisionPlanner
	{
		public FakePlanner()
		{
		}

		public FakePlanner(string response) => Responses.Enqueue(response);

		public Queue<string> Responses { get; } = [];
		public List<IReadOnlyList<ChatMessageInput>> Messages { get; } = [];
		public Func<IReadOnlyList<ChatMessageInput>, CancellationToken, Task<string>> Handler { get; set; } =
			(_, _) => Task.FromResult("{\"status\":\"completed\"}");
		public int CallCount { get; private set; }

		public Task<string> PlanAsync(IReadOnlyList<ChatMessageInput> messages, CancellationToken cancellationToken = default)
		{
			CallCount++;
			Messages.Add(messages);
			if (Responses.Count > 0) return Task.FromResult(Responses.Dequeue());
			return Handler(messages, cancellationToken);
		}
	}
}
