// 体积云渲染
// 通过 raymarch 进行渲染
// ref: https://zhuanlan.zhihu.com/p/248406797
// ref: https://zhuanlan.zhihu.com/p/248965902
// ref: https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-23-high-speed-screen-particles
// ref: http://jcgt.org/published/0007/03/04/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
	[SerializeField]
	[PostProcess(typeof(RiceVolumetricCloudsRenderer), PostProcessEvent.BeforeStack, "Rice/VolumetricClouds", true)]
	public class RiceVolumetricClouds : PostProcessEffectSettings
	{
		public Vector3Parameter cloudsCenterPos = new Vector3Parameter {value = Vector3.zero};
		public Vector3Parameter cloudsSize = new Vector3Parameter {value = Vector3.one};

		public FloatParameter step = new FloatParameter {value = 1.2f};
		[Range(0, 128)]
		public FloatParameter stepCount = new FloatParameter {value = 32.0f};
		[Range(0, 1)]
		public FloatParameter stepLength = new FloatParameter {value = 0.5f};

		public TextureParameter cloudsNoiseTex = new TextureParameter {value = null};
		[Range(0, 1)]
		public FloatParameter cloudsNoiseScale = new FloatParameter {value = 1};
		public Vector3Parameter cloudsNoiseSpeed = new Vector3Parameter {value = Vector3.zero};

		// shade
		public FloatParameter lightAbsorptionTowardSun = new FloatParameter {value = 1};
		public FloatParameter lightAbsorptionThroughCloud = new FloatParameter {value = 1};
		public ColorParameter colorA = new ColorParameter {value = Color.white};
		public ColorParameter colorB = new ColorParameter {value = Color.gray};
		public FloatParameter colorOffsetA = new FloatParameter {value = 1};
		public FloatParameter colorOffsetB = new FloatParameter {value = 1};
		[Range(0, 1)]
		public FloatParameter darknessThreshold = new FloatParameter {value = 0.4f};

		public Vector4Parameter phaseParams = new Vector4Parameter {value = Vector4.one};

		// clouds shape
		public TextureParameter weatherMap = new TextureParameter {value = null};
		public Vector4Parameter shapeNoiseWeights = new Vector4Parameter { value = Vector4.one };
		public FloatParameter densityOffset = new FloatParameter { value = 0 };
		public FloatParameter edgeFadeDst = new FloatParameter {value = 10};
		[Min(0)]
		public FloatParameter densityMultiplier = new FloatParameter {value = 1};

		// detail
		public TextureParameter cloudsNoiseDetailTex = new TextureParameter {value = null};
		[Range(0, 1)]
		public FloatParameter cloudsNoiseDetailScale = new FloatParameter {value = 1};
		public Vector3Parameter cloudsNoiseDetailSpeed = new Vector3Parameter {value = Vector3.zero};
		public FloatParameter cloudsNoiseDetailWeights = new FloatParameter { value = 1};
		public FloatParameter noiseDetailWeight = new FloatParameter {value = 1};

		// blue noise
		public TextureParameter blueNoise = new TextureParameter {value = null};
		[Range(0, 1)]
		public FloatParameter blueNoiseStrength = new FloatParameter {value = 1.0f};

		// downsample
		[Range(1, 16)]
		public IntParameter downSample = new IntParameter {value = 4};
	}

	public class RiceVolumetricCloudsRenderer : PostProcessEffectRenderer<RiceVolumetricClouds>
	{
		public override DepthTextureMode GetCameraFlags()
		{
			return DepthTextureMode.Depth;
		}

		public override void Render(PostProcessRenderContext context)
		{
			if (Application.isPlaying && context.isSceneView)
			{
				context.command.BlitFullscreenTriangle(context.source, context.destination);
				return;
			}

			var sheet = context.propertySheets.Get(Shader.Find("Hidden/PostProcessing/RiceVolumetricClouds"));
			if (sheet != null)
			{
				CommandBuffer cmd = context.command;

				// bounds
				Vector3 sizeOffset = settings.cloudsSize.value * 0.5f;
				Vector3 boundsMin = settings.cloudsCenterPos.value - sizeOffset;
				Vector3 boundsMax = settings.cloudsCenterPos.value + sizeOffset;

				sheet.properties.SetVector("_CloudsBoundsMin", boundsMin);
				sheet.properties.SetVector("_CloudsBoundsMax", boundsMax);
				sheet.properties.SetFloat("_CloudsStep", settings.step);
				sheet.properties.SetFloat("_CloudsRayStepCount", settings.stepCount);
				sheet.properties.SetFloat("_CloudsRayStepLength", settings.stepLength);

				if (settings.cloudsNoiseTex.value != null)
					sheet.properties.SetTexture("_CloudsNoiseTex", settings.cloudsNoiseTex);

				sheet.properties.SetFloat("_CloudsNoiseTexScale", settings.cloudsNoiseScale);
				sheet.properties.SetVector("_CloudsNoiseSpeed", settings.cloudsNoiseSpeed);

				// shade
				sheet.properties.SetFloat("_LightAbsorptionTowardSun", settings.lightAbsorptionTowardSun);
				sheet.properties.SetFloat("_LightAbsorptionThroughCloud", settings.lightAbsorptionThroughCloud);
				sheet.properties.SetColor("_ColA", settings.colorA);
				sheet.properties.SetColor("_ColB", settings.colorB);
				sheet.properties.SetFloat("_ColorOffset1", settings.colorOffsetA);
				sheet.properties.SetFloat("_ColorOffset2", settings.colorOffsetB);
				sheet.properties.SetFloat("_DarknessThreshold", settings.darknessThreshold);

				sheet.properties.SetVector("_PhaseParams", settings.phaseParams);

				// shape
				if (settings.weatherMap.value != null)
					sheet.properties.SetTexture("_WeatherMap", settings.weatherMap);

				sheet.properties.SetVector("_ShapeNoiseWeights", settings.shapeNoiseWeights);
				sheet.properties.SetFloat("_DensityOffset", settings.densityOffset);
				sheet.properties.SetFloat("_EdgeFadeDst", settings.edgeFadeDst);
				sheet.properties.SetFloat("_DensityMultiplier", settings.densityMultiplier);

				if (settings.cloudsNoiseDetailTex.value != null)
					sheet.properties.SetTexture("_CloudsNoiseTexDetail", settings.cloudsNoiseDetailTex);

				sheet.properties.SetFloat("_CloudsNoiseDetailTexScale", settings.cloudsNoiseDetailScale);
				sheet.properties.SetVector("_CloudsNoiseDetailSpeed", settings.cloudsNoiseDetailSpeed);
				sheet.properties.SetFloat("_ShapeNoiseDetailWeights", settings.cloudsNoiseDetailWeights);
				sheet.properties.SetFloat("_NoiseDetailWeight", settings.noiseDetailWeight);

				// blue noise
				if (settings.blueNoise.value != null)
				{
					Vector4 screenUv = new Vector4(
						(float)context.screenWidth / (float)settings.blueNoise.value.width,
						(float)context.screenHeight / (float)settings.blueNoise.value.height, 0, 0);
					sheet.properties.SetVector(Shader.PropertyToID("_CloudsBlueNoiseCoords"), screenUv);
					sheet.properties.SetTexture("_CloudsBlueNoise", settings.blueNoise);
					sheet.properties.SetFloat("_CloudsBlueNoiseStrength", settings.blueNoiseStrength);
				}

				// 降采样
				int width = context.screenWidth / settings.downSample.value;
				int height = context.screenHeight / settings.downSample.value;

				var downSampleDepth = Shader.PropertyToID("_CloudsDepthTmp");
				context.GetScreenSpaceTemporaryRT(cmd, downSampleDepth, 0, context.sourceFormat, RenderTextureReadWrite.Default, FilterMode.Point, width, height);
				cmd.BlitFullscreenTriangle(context.source, downSampleDepth, sheet, 1);
				cmd.SetGlobalTexture("_CloudsDownSampleDepthTex", downSampleDepth);

				var downSampleColor = Shader.PropertyToID("_CloudsColorTmp");
				context.GetScreenSpaceTemporaryRT(cmd, downSampleColor, 0, context.sourceFormat, RenderTextureReadWrite.Default, FilterMode.Bilinear, width, height);
				cmd.BlitFullscreenTriangle(context.source, downSampleColor, sheet, 0);
				cmd.SetGlobalTexture("_CloudsDownSampleColor", downSampleColor);

				// 
				cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

				cmd.ReleaseTemporaryRT(downSampleDepth);
				cmd.ReleaseTemporaryRT(downSampleColor);

				// context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
			}
			else
			{
				context.command.BlitFullscreenTriangle(context.source, context.destination);
			}
		}

		// ------------------------------------------------------------------------
		// gizmos
		public void OnDrawGizmos()
		{
			if (settings.enabled)
			{
				Gizmos.color = Color.green;
				Gizmos.DrawWireCube(settings.cloudsCenterPos, settings.cloudsSize);
			}
		}
	}
}
