using System;
using System.Collections.Generic;
using BSG.CameraEffects;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT;
using EFT.CameraControl;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Killdg.MultipurposeDrone
{
    /// <summary>
    /// Uses the existing full-screen FPS renderer at a remote viewpoint. There
    /// is no second render target, duplicate player or movement of the operator.
    /// Keeping the native camera also keeps its image-effects buffers and texture
    /// streaming controller at the correct resolution and observation position.
    /// </summary>
    internal sealed class DroneCameraView : IDisposable
    {
        private bool zoomEnabled;
        private float nextZoomCapture;
        private RenderTexture? zoomImage;
        internal Texture? ZoomImage => zoomImage;

        private Camera? camera;
        private DroneNightVision? nightVision;
        private bool nightVisionAttempted;
        private global::ThermalVision? thermalVision;
        private EFT.InventoryLogic.ThermalVisionComponent.SelectablePalette originalThermalPalette;
        private bool originalThermalOn;
        internal bool IsNightVisionActive => nightVision != null;
        private Transform? pose;
        private Transform? originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private float originalFieldOfView;
        private bool originalOcclusionCulling;
        private GameObject? lensVolume;
        private PostProcessProfile? lensProfile;
        private LensDistortion? lensDistortion;
        private Player? operatorPlayer;
        private EPointOfView originalBodyView;
        private readonly Dictionary<Renderer, bool> hiddenHands = new();

        internal DroneCameraView()
        {
            Camera.onPreCull += BeforeCull;
        }

        internal void Apply(Transform remotePose, Player player, bool fisheye, bool zoom, bool useNightVision, bool useThermalVision)
        {
            Camera? current = CameraManager.Exist ? CameraManager.Instance.Camera : null;
            if (current == null)
            {
                Release();
                return;
            }

            if (camera != current)
            {
                Release();
                camera = current;
                originalParent = camera.transform.parent;
                originalLocalPosition = camera.transform.localPosition;
                originalLocalRotation = camera.transform.localRotation;
                originalFieldOfView = camera.fieldOfView;
                originalOcclusionCulling = camera.useOcclusionCulling;
                operatorPlayer = player;
                originalBodyView = player.PlayerBody.PointOfView.Value;
            }

            SetFisheye(fisheye);
            SetZoom(zoom);
            if (!useThermalVision)
            {
                ClearThermalVision();
            }
            if (useNightVision)
            {
                if (!nightVisionAttempted)
                {
                    nightVisionAttempted = true;
                    nightVision = DroneNightVision.TryEnable(CameraManager.Instance.NightVision);
                }
            }
            else
            {
                ClearNightVision();
            }
            if (useThermalVision && thermalVision == null)
            {
                EnableThermalVision();
            }
            pose = remotePose;
            ApplyPose();
            // Change only PlayerBody's render state, never Player.PointOfView,
            // movement, health or AI identity. First-person mode makes the
            // operator's head/uniform shadow-only even from a remote camera.
            if (player.PlayerBody.PointOfView.Value != EPointOfView.ThirdPerson)
            {
                player.PlayerBody.UpdatePlayerRenders(EPointOfView.ThirdPerson, player.Side);
            }
            // These are native first-person skin/weapon renderers, not health
            // colliders or AI perception. Restore each original rendering flag
            // when the inventory opens or the feed disconnects.
            Hide(CameraManager.Instance.handsRenderers);
        }

        private void SetZoom(bool enabled)
        {
            zoomEnabled = enabled;
            if (!enabled)
            {
                ClearZoom();
            }
        }

        internal void CaptureZoomFrame()
        {
            if (!zoomEnabled || camera == null || Time.unscaledTime < nextZoomCapture)
            {
                return;
            }

            nextZoomCapture = Time.unscaledTime + 1f / 30f;
            int width = Screen.width;
            int height = Screen.height;
            if (zoomImage == null || zoomImage.width != width || zoomImage.height != height)
            {
                ClearZoom();
                zoomImage = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
                {
                    name = "Drone inset frame capture",
                    filterMode = FilterMode.Bilinear
                };
                zoomImage.Create();
            }

            // The game resolves its camera through post-processing before
            // presentation. Read the composed frame after that resolve rather
            // than sampling CameraTarget, which can be empty in this pipeline.
            ScreenCapture.CaptureScreenshotIntoRenderTexture(zoomImage);
        }

        private void ClearZoom()
        {
            if (zoomImage != null)
            {
                zoomImage.Release();
                UnityEngine.Object.Destroy(zoomImage);
                zoomImage = null;
            }
        }

        private void SetFisheye(bool enabled)
        {
            if (!enabled)
            {
                ClearFisheye();
                return;
            }

            if (lensVolume != null || camera == null
                || !camera.TryGetComponent(out PostProcessLayer layer) || !layer.enabled)
            {
                return;
            }

            int mask = layer.volumeLayer.value;
            if (mask == 0)
            {
                return;
            }

            int volumeLayer = 0;
            while ((mask & (1 << volumeLayer)) == 0)
            {
                volumeLayer++;
            }

            // Add only distortion to the native renderer's existing stack.
            // The volume lives for this feed and never edits a game profile.
            lensProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
            lensDistortion = lensProfile.AddSettings<LensDistortion>();
            lensDistortion.enabled.Override(true);
            lensDistortion.intensity.Override(-12f);
            lensVolume = new GameObject("Recon Drone Lens");
            lensVolume.layer = volumeLayer;
            var volume = lensVolume.AddComponent<PostProcessVolume>();
            volume.sharedProfile = lensProfile;
            volume.isGlobal = true;
            volume.priority = 1000f;
        }

        private void ClearFisheye()
        {
            if (lensVolume != null)
            {
                lensVolume.SetActive(false);
                UnityEngine.Object.Destroy(lensVolume);
                lensVolume = null;
            }

            if (lensDistortion != null)
            {
                UnityEngine.Object.Destroy(lensDistortion);
                lensDistortion = null;
            }

            if (lensProfile != null)
            {
                UnityEngine.Object.Destroy(lensProfile);
                lensProfile = null;
            }
        }

        private void Hide(Renderer[]? renderers)
        {
            if (renderers == null)
            {
                return;
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!hiddenHands.ContainsKey(renderer))
                {
                    hiddenHands[renderer] = renderer.forceRenderingOff;
                }

                renderer.forceRenderingOff = true;
            }
        }

        private void BeforeCull(Camera candidate)
        {
            if (candidate == camera)
            {
                // Native animation/camera scripts may update after LateUpdate.
                // Reapply immediately before this camera's regular render.
                ApplyPose();
            }
        }

        internal void RefreshBeforeVisibility()
        {
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (camera == null || pose == null)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.fieldOfView = originalFieldOfView;
            // The native occlusion bake assumes a ground-level player camera.
            // Keep frustum culling, but avoid using that ground bake for an airborne
            // view. This flag belongs to this leased camera and is restored below.
            camera.useOcclusionCulling = false;
            camera.ResetWorldToCameraMatrix();
            camera.ResetProjectionMatrix();
            camera.ResetCullingMatrix();
        }

        private void EnableThermalVision()
        {
            var effect = CameraManager.Instance.ThermalVision;
            if (effect == null || !effect.isActiveAndEnabled || effect.TextureMask == null
                || effect.ThermalVisionUtilities == null)
            {
                return;
            }

            // Reuse the game's heat rendering and white-hot ramp on the
            // drone camera. The optic's housing and reticle belong to its scope.
            effect.FastForwardSwitch();
            thermalVision = effect;
            originalThermalOn = effect.On;
            originalThermalPalette = effect.ThermalVisionUtilities.CurrentRampPalette;
            effect.ThermalVisionUtilities.CurrentRampPalette =
                EFT.InventoryLogic.ThermalVisionComponent.SelectablePalette.WhiteHot;
            if (!originalThermalOn)
            {
                effect.Switch(true);
            }
            effect.SetMaterialProperties();
        }

        private void ClearThermalVision()
        {
            if (thermalVision != null)
            {
                thermalVision.ThermalVisionUtilities.CurrentRampPalette = originalThermalPalette;
                if (thermalVision.On != originalThermalOn)
                {
                    thermalVision.Switch(originalThermalOn);
                }
                thermalVision.SetMaterialProperties();
                thermalVision = null;
            }
        }
        private void ClearNightVision()
        {
            nightVision?.Dispose();
            nightVision = null;
            nightVisionAttempted = false;
        }

        internal void Release()
        {
            SetZoom(false);
            ClearThermalVision();
            ClearNightVision();
            ClearFisheye();
            pose = null;
            try
            {
                if (camera != null)
                {
                    // Do not reparent anything. A different native parent means
                    // the game has already replaced its camera ownership.
                    if (camera.transform.parent == originalParent)
                    {
                        camera.transform.localPosition = originalLocalPosition;
                        camera.transform.localRotation = originalLocalRotation;
                    }

                    camera.fieldOfView = originalFieldOfView;
                    camera.useOcclusionCulling = originalOcclusionCulling;
                    camera.ResetWorldToCameraMatrix();
                    camera.ResetProjectionMatrix();
                    camera.ResetCullingMatrix();
                }
            }
            finally
            {
                if (operatorPlayer != null && operatorPlayer.PlayerBody != null)
                {
                    operatorPlayer.PlayerBody.UpdatePlayerRenders(originalBodyView, operatorPlayer.Side);
                }

                operatorPlayer = null;
                foreach (KeyValuePair<Renderer, bool> pair in hiddenHands)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.forceRenderingOff = pair.Value;
                    }
                }

                hiddenHands.Clear();
                camera = null;
                originalParent = null;
            }
        }

        public void Dispose()
        {
            Release();
            Camera.onPreCull -= BeforeCull;
        }
    }
}
