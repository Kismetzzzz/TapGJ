using UnityEngine;
using PandaTitle;

[ExecuteInEditMode]
public class PandaPostProcess : MonoBehaviour
{
	public Material PostProcessMat;

	[DisplayName("像素剔除程度")] [Range(0, 1)] public float StepFactor = 0.5F;
    [DisplayName("总透明度")] [Range(0, 1)] public float MainAlpha=1F;
	[DisplayName("径向模糊强度")] [Range(0, 1)] public float BlurFactor;
	[DisplayName("UV拉伸强度")] [Range(0, 4)] public float LineUVScale;
	[DisplayName("色散强度")] [Range(0, 1.5F)] public float Chromatic;
	[DisplayName("振频")] [Range(0, 1)] public float Frequency;
	[DisplayName("振幅")] [Range(0, 1)] public float Amplitude;
	[DisplayName("黑边框宽度")] [Range(1, 3)] public float VignettePower=1.5F;
	[DisplayName("黑边框强度")] [Range(0, 3)] public float VignetteScale= 1.5F;
	private void Awake()
	{
		if( PostProcessMat == null )
		{
			enabled = false;
		}
		else
		{
			
			PostProcessMat.mainTexture = PostProcessMat.mainTexture;
		}

	}

	//void Start()
	//{
	
	//var BlurFactor = PostProcessMat.GetFloat("_BlurFactorK");


	//}

	//void Update()
	//{
		

	
	//}

	void OnRenderImage( RenderTexture src, RenderTexture dest )
	{
		PostProcessMat.SetFloat("_StepFactorK", StepFactor);

		PostProcessMat.SetFloat("_BlurFactorK", BlurFactor);
		
		PostProcessMat.SetFloat("_LineUVScaleK", LineUVScale);

		PostProcessMat.SetFloat("_MainAlphaK", MainAlpha);

		PostProcessMat.SetFloat("_zhenpinK", Frequency);

		PostProcessMat.SetFloat("_zhenfuK", Amplitude);
		PostProcessMat.SetFloat("_RedBlueFactorK", Chromatic);
		PostProcessMat.SetFloat("_VignettePowerK", VignettePower);
		PostProcessMat.SetFloat("_VignetteScaleK", VignetteScale);
		Graphics.Blit( src, dest, PostProcessMat );
	}
}
