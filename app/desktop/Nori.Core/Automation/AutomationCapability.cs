namespace Nori.Core.Automation;

/// <summary>自动化动作需要的能力标志。</summary>
[Flags]
public enum AutomationCapability
{
	/// <summary>没有能力。</summary>
	None = 0,
	/// <summary>鼠标指针操作。</summary>
	Pointer = 1 << 0,
	/// <summary>键盘操作。</summary>
	Keyboard = 1 << 1,
	/// <summary>滚轮操作。</summary>
	Scroll = 1 << 2,
}

/// <summary>允许的自动化动作种类。</summary>
public enum AutomationActionKind
{
	/// <summary>单击屏幕坐标。</summary>
	Click,
	/// <summary>输入文本。</summary>
	TypeText,
	/// <summary>按下一个受限的键。</summary>
	KeyPress,
	/// <summary>滚动滚轮。</summary>
	Scroll,
}
