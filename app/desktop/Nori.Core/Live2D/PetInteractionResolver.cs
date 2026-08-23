namespace Nori.Core.Live2D;

/// <summary>自定义矩形互动区域的命中结果。</summary>
public sealed record PetInteractionHit(
	PetInteractionRegion Region,
	double ModelX,
	double ModelY,
	double RegionX,
	double RegionY);

/// <summary>交给应用运行时处理的 AI 互动触发信息。</summary>
public sealed record PetInteractionTrigger(
	string ModelId,
	long ModelGeneration,
	PetInteractionHit Hit);

/// <summary>
/// 根据模型画布归一化坐标解析自定义互动区域。
/// 重叠时选择面积最小的区域；面积相同时保持配置列表顺序。
/// </summary>
public static class PetInteractionResolver
{
	public static bool TryResolve(
		PetInteractionConfig config,
		double modelX,
		double modelY,
		out PetInteractionHit? hit)
	{
		hit = null;
		if (!double.IsFinite(modelX) || !double.IsFinite(modelY)
			|| modelX < 0 || modelX > 1 || modelY < 0 || modelY > 1)
		{
			return false;
		}

		PetInteractionRegion? selected = null;
		double selectedArea = double.MaxValue;
		if (config.Regions is null) return false;
		foreach (PetInteractionRegion region in config.Regions)
		{
			if (!region.Rect.Contains(modelX, modelY)) continue;
			double area = region.Rect.Area;
			if (selected is not null && area >= selectedArea) continue;
			selected = region;
			selectedArea = area;
		}

		if (selected is null) return false;
		double regionX = Math.Clamp((modelX - selected.Rect.X) / selected.Rect.Width, 0, 1);
		double regionY = Math.Clamp((modelY - selected.Rect.Y) / selected.Rect.Height, 0, 1);
		hit = new PetInteractionHit(selected, modelX, modelY, regionX, regionY);
		return true;
	}
}
