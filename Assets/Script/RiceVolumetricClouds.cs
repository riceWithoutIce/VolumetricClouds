// 体积云渲染
// 通过 raymarch 进行渲染
// ref: https://zhuanlan.zhihu.com/p/248406797
// ref: https://zhuanlan.zhihu.com/p/248965902
// ref: https://developer.nvidia.com/gpugems/gpugems3/part-iv-image-effects/chapter-23-high-speed-screen-particles
// ref: http://jcgt.org/published/0007/03/04/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
	[SerializeField]
	[PostProcess(typeof(RiceVolumetricCloudsRenderer), PostProcessEvent.BeforeStack, "Rice/VolumetricClouds")]
	public class RiceVolumetricClouds : PostProcessEffectSettings
	{
		public enum eDownSample
		{
			eDS_1 = 1,
			eDS_2 = 2,
			eDS_4 = 4,
			eDS_8 = 8,
			eDS_16 = 16,
			eDS_32 = 32,
		}

		[Serializable]
		public sealed class DownSampleParameter : ParameterOverride<eDownSample> { }

		public Vector3Parameter CloudsCenterPos = new Vector3Parameter { value = Vector3.zero };
		public Vector3Parameter CloudsSize = new Vector3Parameter { value = Vector3.one };

		[Header("Raymarch")]
		[Range(0, 32)]
		public FloatParameter Steps = new FloatParameter { value = 32.0f };

		[Header("Lighting")]
		[Range(0, 20)]
		public FloatParameter LightAbsorptionTowardSun = new FloatParameter { value = 1 };
		[Range(0, 5)]
		public FloatParameter LightAbsorptionThroughCloud = new FloatParameter { value = 1 };
		[ColorUsage(true, true)]
		public ColorParameter ColorA = new ColorParameter { value = Color.white };
		[ColorUsage(true, true)]
		public ColorParameter ColorB = new ColorParameter { value = Color.gray };
		[Range(0, 1)]
		public FloatParameter MieScatter = new FloatParameter { value = 0.5f };
		[Range(0, 16)] 
		public FloatParameter LightingSteps = new FloatParameter {value = 4};

		[Header("CloudsShape")]
		public TextureParameter WeatherMap = new TextureParameter { value = null };
		[Min(0)]
		public FloatParameter WeatherMapSize = new FloatParameter { value = 1 };
		public Vector2Parameter CloudsSpeed = new Vector2Parameter { value = Vector2.zero };
		[Min(0)]
		public FloatParameter EdgeFadeDst = new FloatParameter { value = 10 };
		[Range(0, 2)]
		public FloatParameter ShapeNoiseWeight = new FloatParameter { value = 1.0f };
		[Range(-1, 1)]
		public FloatParameter DensityOffset = new FloatParameter { value = 0 };
		[Range(0, 10)]
		public FloatParameter DensityMultiplier = new FloatParameter { value = 1 };

		[Header("CloudsNoise")]
		public TextureParameter CloudsNoiseTex = new TextureParameter { value = null };
		[Range(0, 0.5f)]
		public FloatParameter CloudsNoiseScale = new FloatParameter { value = 1 };
		[Range(0, 0.1f)]
		public FloatParameter CloudsNoiseStrength = new FloatParameter { value = 0.1f };
		public Vector3Parameter CloudsNoiseSpeed = new Vector3Parameter { value = Vector3.zero };

		[Header("Detail")]
		public TextureParameter CloudsNoiseDetailTex = new TextureParameter { value = null };
		[Range(0, 0.5f)]
		public FloatParameter CloudsNoiseDetailScale = new FloatParameter { value = 1 };
		public Vector3Parameter CloudsNoiseDetailSpeed = new Vector3Parameter { value = Vector3.zero };
		[Min(0)]
		public FloatParameter CloudsNoiseDetailShape = new FloatParameter { value = 1 };
		[Range(0, 1)]
		public FloatParameter NoiseDetailWeight = new FloatParameter { value = 1 };

		[Header("BlueNoise")]
		[Range(0, 10)]
		public FloatParameter BlueNoiseScale = new FloatParameter { value = 0.5f };
		[Range(0, 10)]
		public FloatParameter BlueNoiseStrength = new FloatParameter { value = 1.0f };

		[Header("DownSample")]
		[Range(1, 16)]
		public DownSampleParameter DownSample = new DownSampleParameter { value = eDownSample.eDS_2 };
	}

	public class RiceVolumetricCloudsRenderer : PostProcessEffectRenderer<RiceVolumetricClouds>
	{
		private const int HISTORY = 4;
		private int _blueNoiseIndex = 0;
		private int _curSample = 0;

		private Vector3 _preCamPos = Vector3.zero;
		private Quaternion _preCamRot = Quaternion.identity;

		private int _width, _height;

		private int _history;
		private int _downSampleColor;

		public override DepthTextureMode GetCameraFlags()
		{
			return DepthTextureMode.Depth;
		}

		public override void Init()
		{
			_history = Shader.PropertyToID("_CloudsHistoryTex");
			_downSampleColor = Shader.PropertyToID("_CloudsColorTmp");
		}

		public override void Render(PostProcessRenderContext context)
		{
			var sheet = context.propertySheets.Get(Shader.Find("Hidden/PostProcessing/RiceVolumetricClouds"));
			if (sheet != null)
			{
				CommandBuffer cmd = context.command;

				cmd.BeginSample("Volumetric clouds");

				// bounds
				Vector3 sizeOffset = settings.CloudsSize.value * 0.5f;
				Vector3 boundsMin = settings.CloudsCenterPos.value - sizeOffset;
				Vector3 boundsMax = settings.CloudsCenterPos.value + sizeOffset;

				sheet.properties.SetVector("_CloudsBoundsMin", boundsMin);
				sheet.properties.SetVector("_CloudsBoundsMax", boundsMax);
				sheet.properties.SetFloat("_CloudsRayStepCount", settings.Steps);

				if (settings.CloudsNoiseTex.value != null)
					sheet.properties.SetTexture("_CloudsNoiseTex", settings.CloudsNoiseTex);

				sheet.properties.SetFloat("_CloudsNoiseTexScale", settings.CloudsNoiseScale);
				sheet.properties.SetFloat("_CloudsNoiseTexStrength", settings.CloudsNoiseStrength);
				sheet.properties.SetVector("_CloudsNoiseSpeed", settings.CloudsNoiseSpeed);

				// shade
				sheet.properties.SetFloat("_LightAbsorptionTowardSun", settings.LightAbsorptionTowardSun);
				sheet.properties.SetFloat("_LightAbsorptionThroughCloud", settings.LightAbsorptionThroughCloud);
				sheet.properties.SetColor("_ColA", settings.ColorA);
				sheet.properties.SetColor("_ColB", settings.ColorB);
				sheet.properties.SetFloat("_MieScatter", settings.MieScatter);
				sheet.properties.SetFloat("_LightingSteps", settings.LightingSteps);

				// shape
				if (settings.WeatherMap.value != null)
					sheet.properties.SetTexture("_WeatherMap", settings.WeatherMap);

				sheet.properties.SetFloat("_WeatherMapSize", settings.WeatherMapSize);
				sheet.properties.SetFloat("_ShapeNoiseWeight", settings.ShapeNoiseWeight);
				sheet.properties.SetFloat("_DensityOffset", settings.DensityOffset);
				sheet.properties.SetFloat("_EdgeFadeDst", settings.EdgeFadeDst);
				sheet.properties.SetFloat("_DensityMultiplier", settings.DensityMultiplier);
				sheet.properties.SetVector("_CloudsSpeed", settings.CloudsSpeed);

				if (settings.CloudsNoiseDetailTex.value != null)
					sheet.properties.SetTexture("_CloudsNoiseTexDetail", settings.CloudsNoiseDetailTex);

				sheet.properties.SetFloat("_CloudsNoiseDetailTexScale", settings.CloudsNoiseDetailScale);
				sheet.properties.SetVector("_CloudsNoiseDetailSpeed", settings.CloudsNoiseDetailSpeed);
				sheet.properties.SetFloat("_ShapeNoiseDetailWeights", settings.CloudsNoiseDetailShape);
				sheet.properties.SetFloat("_NoiseDetailWeight", settings.NoiseDetailWeight);

				// 降采样
				int downSample = (int)settings.DownSample.value;
				int width = context.screenWidth / downSample;
				int height = context.screenHeight / downSample;

				// blue noise
				Texture2D blueNoise = context.resources.blueNoise256[_blueNoiseIndex];
				_blueNoiseIndex++;
				_blueNoiseIndex = _blueNoiseIndex % context.resources.blueNoise256.Length;
				// _blueNoiseIndex = 0;
				if (blueNoise != null)
				{
					Vector4 screenUv = new Vector4(
						((float)width / (float)blueNoise.width) * settings.BlueNoiseScale,
						((float)height / (float)blueNoise.height) * settings.BlueNoiseScale, 0, 0);
					sheet.properties.SetVector(Shader.PropertyToID("_CloudsBlueNoiseCoords"), screenUv);
					sheet.properties.SetTexture("_CloudsBlueNoise", blueNoise);
					sheet.properties.SetFloat("_CloudsBlueNoiseStrength", settings.BlueNoiseStrength);
				}

				// sample
				_curSample++;
				_curSample = Mathf.Min(_curSample, HISTORY);
				// _curSample = 0;
				if (_preCamPos != context.camera.transform.position || _preCamRot != context.camera.transform.rotation)
				{
					_preCamPos = context.camera.transform.position;
					_preCamRot = context.camera.transform.rotation;
					_curSample = 0;
				}

				sheet.properties.SetFloat("_CloudsSample", _curSample);

				// history
				context.GetScreenSpaceTemporaryRT(cmd, _history, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, width, height);
				cmd.SetGlobalTexture("_CloudsHistoryTex", _history);

				//				var downSampleDepth = Shader.PropertyToID("_CloudsDepthTmp");
				//				if (downSample > (int) YoukiaVolumetricClouds.eDownSample.eDS_1)
				//				{
				//					context.GetScreenSpaceTemporaryRT(cmd, downSampleDepth, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default, FilterMode.Bilinear, width, height);
				//					cmd.BlitFullscreenTriangle(context.source, downSampleDepth, sheet, 1);
				//					cmd.SetGlobalTexture("_CloudsDownSampleDepthTex", downSampleDepth);
				//					sheet.EnableKeyword("_VC_DOWNSAMPLE");
				//				}
				//				else
				//				{
				//					sheet.DisableKeyword("_VC_DOWNSAMPLE");
				//				}

				context.GetScreenSpaceTemporaryRT(cmd, _downSampleColor, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default, FilterMode.Bilinear, width, height);
				// cmd.BlitFullscreenTriangle(context.source, _downSampleColor, sheet, 0);
				cmd.BlitFullscreenTriangle(_history, _downSampleColor, sheet, 0);
				cmd.BlitFullscreenTriangle(_downSampleColor, _history);
				cmd.SetGlobalTexture("_CloudsDownSampleColor", _downSampleColor);

				// 
				cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

				//				cmd.ReleaseTemporaryRT(downSampleDepth);
				cmd.ReleaseTemporaryRT(_downSampleColor);
				cmd.ReleaseTemporaryRT(_history);

				// context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);

				cmd.EndSample("Volumetric clouds");

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
				Gizmos.DrawWireCube(settings.CloudsCenterPos, settings.CloudsSize);
			}
		}
	}
}
