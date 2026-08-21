namespace Nori.Desktop.Live2D.Behaviors;

/// <summary>
/// 模型参数基准值
///
/// 对应前端 stores/model-parameters.ts
/// </summary>
public sealed class ModelParameters
{
	public float AngleX { get; set; }
	public float AngleY { get; set; }
	public float AngleZ { get; set; }
	public float LeftEyeOpen { get; set; } = 1.0f;
	public float RightEyeOpen { get; set; } = 1.0f;
	public float LeftEyeSmile { get; set; }
	public float RightEyeSmile { get; set; }
	public float LeftEyebrowLR { get; set; }
	public float RightEyebrowLR { get; set; }
	public float LeftEyebrowY { get; set; }
	public float RightEyebrowY { get; set; }
	public float LeftEyebrowAngle { get; set; }
	public float RightEyebrowAngle { get; set; }
	public float LeftEyebrowForm { get; set; }
	public float RightEyebrowForm { get; set; }
	public float MouthOpen { get; set; }
	public float MouthForm { get; set; }
	public float Cheek { get; set; }
	public float BodyAngleX { get; set; }
	public float BodyAngleY { get; set; }
	public float BodyAngleZ { get; set; }
	public float Breath { get; set; }

	public void Reset()
	{
		AngleX = 0;
		AngleY = 0;
		AngleZ = 0;
		LeftEyeOpen = 1.0f;
		RightEyeOpen = 1.0f;
		LeftEyeSmile = 0;
		RightEyeSmile = 0;
		LeftEyebrowLR = 0;
		RightEyebrowLR = 0;
		LeftEyebrowY = 0;
		RightEyebrowY = 0;
		LeftEyebrowAngle = 0;
		RightEyebrowAngle = 0;
		LeftEyebrowForm = 0;
		RightEyebrowForm = 0;
		MouthOpen = 0;
		MouthForm = 0;
		Cheek = 0;
		BodyAngleX = 0;
		BodyAngleY = 0;
		BodyAngleZ = 0;
		Breath = 0;
	}
}
