﻿using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using ModelViewer.HelperSuite.ContentLoader;
using ModelViewer.HelperSuite.Static;
using ModelViewer.Logic;
using ModelViewer.Renderer.ShaderModules;
using ModelViewer.Renderer.ShaderModules.Helper;

namespace ModelViewer.Renderer
{
    public class Renderer
    {
        private GraphicsDevice _graphics;
        private SpriteBatch _spriteBatch;

        private Matrix _view;
        private Matrix _projection;
        private Matrix _viewProjection;
        private bool _viewProjectionHasChanged;

        private ThreadSafeContentManager _contentManager;

        private RenderTarget2D _linearDepthTarget;
        private DepthStencilState _stencilWriteOnly;
        private DepthStencilState _stencilReadOnly;
        private BlendState _subtractive;
        private int _aoSamples;
        private float _aoRadii;
        private float _aoStrength;


        private Texture2D rollTexture2D;
        private TextureCube skyboxCube;
        private Texture fresnelMap;

        private readonly Vector3[] _cornersWorldSpace = new Vector3[8];
        private readonly Vector3[] _cornersViewSpace = new Vector3[8];
        private readonly Vector3[] _currentFrustumCorners = new Vector3[4];

        private Model model;
        private Matrix YupOrientation;

        //Modules
        private SkyboxRenderModule _skyboxRenderModule;
        private AnimatedModelShader _animatedModelShader;
        private AmbientOcclusionShader _ambientOcclusionShader;

        public void Initialize(GraphicsDevice graphicsDevice)
        {
            _graphics = graphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            
            _skyboxRenderModule.Initialize(_graphics);
            _animatedModelShader.Initialize(_graphics);
            _ambientOcclusionShader.Initialize(_graphics);

            YupOrientation = Matrix.CreateRotationX((float) (Math.PI/2));

            UpdateRenderTargets();

            _stencilWriteOnly = new DepthStencilState()
            {
                DepthBufferEnable = true,
                DepthBufferWriteEnable = true,
                CounterClockwiseStencilFunction = CompareFunction.Always,
                StencilFunction = CompareFunction.Always,
                StencilFail = StencilOperation.IncrementSaturation,
                StencilPass = StencilOperation.IncrementSaturation,
                CounterClockwiseStencilFail = StencilOperation.IncrementSaturation,
                CounterClockwiseStencilPass = StencilOperation.IncrementSaturation,
                ReferenceStencil = 0,
                StencilEnable = true,
                StencilMask = 0,
            };

            _stencilReadOnly = new DepthStencilState()
            {
                DepthBufferEnable = false,
                DepthBufferWriteEnable = false,
                CounterClockwiseStencilFunction = CompareFunction.Less,
                StencilFunction = CompareFunction.Less,
                StencilFail = StencilOperation.Keep,
                StencilPass = StencilOperation.Keep,
                CounterClockwiseStencilFail = StencilOperation.Keep,
                CounterClockwiseStencilPass = StencilOperation.Keep,
                ReferenceStencil = 0,
                StencilEnable = true
            };

            _subtractive = new BlendState()
            {
                ColorSourceBlend = Blend.InverseSourceAlpha,
                AlphaSourceBlend = Blend.Zero,
                ColorDestinationBlend = Blend.One,
                AlphaDestinationBlend = Blend.One,
                ColorBlendFunction = BlendFunction.ReverseSubtract,
                AlphaBlendFunction = BlendFunction.ReverseSubtract,
              
            };
        }

        public void Load(ContentManager contentManager)
        {
            _contentManager = new ThreadSafeContentManager(contentManager.ServiceProvider) { RootDirectory = "Content" };

            rollTexture2D = _contentManager.Load<Texture2D>("Graphical User Interface/ring");
            skyboxCube = _contentManager.Load<TextureCube>("ShaderModules/Skybox/skyboxCubemap");
            fresnelMap = _contentManager.Load<Texture>("ShaderModules/AnimatedModelShader/fresnel2");

            model = _contentManager.Load<Model>("ShaderModules/Skybox/isosphere"/*"ShaderModules/AnimatedModelShader/cube"*/);

            _skyboxRenderModule = new SkyboxRenderModule();
            _skyboxRenderModule.Load(_contentManager, "ShaderModules/Skybox/skybox", "ShaderModules/Skybox/isosphere");
            _skyboxRenderModule.SetSkybox(skyboxCube);

            _animatedModelShader = new AnimatedModelShader();
            _animatedModelShader.Load(_contentManager, "ShaderModules/AnimatedModelShader/AnimatedModelShader");
            _animatedModelShader.EnvironmentMap = skyboxCube;
            _animatedModelShader.FresnelMap = fresnelMap;

            _ambientOcclusionShader = new AmbientOcclusionShader();
            _ambientOcclusionShader.Load(_contentManager, "ShaderModules/AmbientOcclusionShader/AmbientOcclusionShader");
        }

        public void Draw(Camera camera, MainLogic mainLogic, Vector3 modelPosition, GameTime gameTime)
        {
            float scale = (float)Math.Pow(10, GameSettings.m_size);

            CheckRenderChanges(scale);

            UpdateViewProjection(camera);

            AnimatedModelShader.EffectPasses pass = AnimatedModelShader.EffectPasses.Unskinned;

            _graphics.BlendState = BlendState.Opaque;
            _graphics.DepthStencilState = DepthStencilState.Default;

            float meshsize = 0.5f;

            object loadedModel = mainLogic.modelLoader.LoadedObject;
            AnimatedModel usedModel = loadedModel != null ? (AnimatedModel)loadedModel : null;

            if (usedModel != null)
            {
                meshsize = 1 / usedModel.Model.Meshes[0].BoundingSphere.Radius;
            }

            {
                object loadedMaterial = mainLogic.albedoLoader.LoadedObject;
                Texture2D loadedAlbedo = loadedMaterial != null ? (Texture2D) loadedMaterial : null;
                _animatedModelShader.AlbedoMap = loadedAlbedo;
            }

            {
                object loadedMaterial = mainLogic.normalLoader.LoadedObject;
                Texture2D loadedNormal = loadedMaterial != null ? (Texture2D)loadedMaterial : null;
                _animatedModelShader.NormalMap = loadedNormal;

                if(loadedNormal!=null)
                pass = AnimatedModelShader.EffectPasses.UnskinnedNormalMapped;
            }
            {
                object loadedMaterial = mainLogic.roughnessLoader.LoadedObject;
                Texture2D loadedAlbedo = loadedMaterial != null ? (Texture2D)loadedMaterial : null;
                _animatedModelShader.RoughnessMap = loadedAlbedo;
            }
            {
                object loadedMaterial = mainLogic.metallicLoader.LoadedObject;
                Texture2D loadedAlbedo = loadedMaterial != null ? (Texture2D)loadedMaterial : null;
                _animatedModelShader.MetallicMap = loadedAlbedo;
            }


            Matrix size = Matrix.CreateScale(scale);
            Matrix meshscale = Matrix.CreateScale(meshsize);

            Matrix world = meshscale * (GameSettings.m_orientationy ? YupOrientation : Matrix.Identity) * Matrix.CreateTranslation(/*-usedModel.Meshes[0].BoundingSphere.Center*/ - modelPosition/ scale) * size ;

            _animatedModelShader.AlbedoColor = GameSettings.bgColor;
            _animatedModelShader.Roughness = GameSettings.m_roughness;
            _animatedModelShader.Metallic = GameSettings.m_metallic;
            _animatedModelShader.UseLinear = GameSettings.r_UseLinear;

            if (loadedModel != null)
            {
                if (GameSettings.r_DrawAo )
                {
                    //Draw to depth buffer first!
                    _graphics.SetRenderTarget(_linearDepthTarget);
                    _graphics.BlendState = BlendState.Opaque;
                    _graphics.Clear(Color.White);
                    usedModel.Draw(world, _view, _viewProjection, camera.Position, _animatedModelShader, AnimatedModelShader.EffectPasses.UnskinnedDepth, true);

                    _graphics.SetRenderTarget(null);
                    //_graphics.Clear(ClearOptions.Stencil | ClearOptions.Target, Color.Red, 0, 0);
                    _graphics.DepthStencilState = _stencilWriteOnly;
                    usedModel.Draw(world, _view, _viewProjection, camera.Position, _animatedModelShader, pass, false);
                }
                else
                {
                    usedModel.Draw(world, _view, _viewProjection, camera.Position, _animatedModelShader, pass, true);
                }

                if (usedModel.HasModelExtra())
                {
                    if (usedModel.Clips.Count > 0 && GameSettings.m_updateAnimation)
                        usedModel.Update(gameTime);

                    if (GameSettings.m_startClip)
                    {
                        usedModel.PlayClip(usedModel.Clips[0], true);
                        GameSettings.m_startClip = false;
                    }
                }

            }
            else
            {
                _graphics.SetRenderTarget(null);
                _graphics.BlendState = BlendState.Opaque;
                _graphics.Clear(Color.White);
                _animatedModelShader.DrawMesh(model, world, _view, _viewProjection, camera.Position, pass);
            }

            if (GameSettings.r_DrawAo)
            {
                _graphics.BlendState = _subtractive;

                _graphics.DepthStencilState = _stencilReadOnly;
                _graphics.RasterizerState = RasterizerState.CullCounterClockwise;
                _ambientOcclusionShader.Draw(null);
            }

            _graphics.BlendState = BlendState.Opaque;
            _graphics.RasterizerState = RasterizerState.CullClockwise;
            _graphics.DepthStencilState = DepthStencilState.Default;
            _skyboxRenderModule.Draw(Matrix.CreateTranslation(camera.Position) *  _viewProjection, Vector3.Zero, 300);

            if (GameSettings.r_DrawDepthMap)
            {
                _graphics.SetRenderTarget(null);
                _graphics.BlendState = BlendState.Opaque;
                _graphics.RasterizerState = RasterizerState.CullCounterClockwise;
                _spriteBatch.Begin();
                _spriteBatch.Draw(_linearDepthTarget, new Rectangle(0,0,GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight), Color.White);
                _spriteBatch.End();
            }

            DrawInteractivityAnimation(gameTime);

            
        }

        private void CheckRenderChanges(float scale)
        {
            if (GameSettings.ao_Samples != _aoSamples)
            {
                _aoSamples = GameSettings.ao_Samples;
                _ambientOcclusionShader.Samples = _aoSamples;
            }
            if (GameSettings.ao_Radii * scale != _aoRadii)
            {
                _aoRadii = GameSettings.ao_Radii;
                _ambientOcclusionShader.SampleRadii = _aoRadii;
            }
            if (GameSettings.ao_Strength != _aoStrength)
            {
                _aoStrength = GameSettings.ao_Strength;
                _ambientOcclusionShader.SampleStrength = _aoStrength;
            }
        }

        private void DrawInteractivityAnimation(GameTime gameTime)
        {
            _spriteBatch.Begin();
            
            _spriteBatch.Draw(rollTexture2D, new Rectangle(10, GameSettings.g_ScreenHeight - 80, 20, 20), null, Color.White, (float)-gameTime.TotalGameTime.TotalSeconds * 3, new Vector2(rollTexture2D.Width / 2, rollTexture2D.Height / 2), SpriteEffects.None, 0);

            _spriteBatch.End();

        }

        private void UpdateViewProjection(Camera camera)
        {
            _viewProjectionHasChanged = camera.HasChanged;

            //If the camera didn't do anything we don't need to update this stuff
            if (_viewProjectionHasChanged)
            {
                //We have processed the change, now setup for next frame as false
                camera.HasChanged = false;
                camera.HasMoved = false;

                //View matrix
                _view = Matrix.CreateLookAt(camera.Position, camera.Lookat, camera.Up);

                _projection = Matrix.CreatePerspectiveFieldOfView(camera.FieldOfView,
                    GameSettings.g_ScreenWidth / (float)GameSettings.g_ScreenHeight, 1, GameSettings.g_FarPlane);
                //_projection = Matrix.CreateOrthographic(GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, -100, 100);

                _viewProjection = _view * _projection;
            }

            BoundingFrustum _boundingFrustum = new BoundingFrustum(Matrix.Identity);
            _boundingFrustum.Matrix = _viewProjection;
            ComputeFrustumCorners(_boundingFrustum);

        }

        /// <summary>
        /// From https://jcoluna.wordpress.com/2011/01/18/xna-4-0-light-pre-pass/
        /// Compute the frustum corners for a camera.
        /// Its used to reconstruct the pixel position using only the depth value.
        /// Read here for more information
        /// http://mynameismjp.wordpress.com/2009/03/10/reconstructing-position-from-depth/
        /// </summary>
        /// <param name="cameraFrustum"></param>
        private void ComputeFrustumCorners(BoundingFrustum cameraFrustum)
        {
            cameraFrustum.GetCorners(_cornersWorldSpace);
            //this is the inverse of our camera transform
            Vector3.Transform(_cornersWorldSpace, ref _view, _cornersViewSpace); //put the frustum into view space
            for (int i = 0; i < 4; i++) //take only the 4 farthest points
            {
                _currentFrustumCorners[i] = _cornersViewSpace[i + 4];
            }
            Vector3 temp = _currentFrustumCorners[3];
            _currentFrustumCorners[3] = _currentFrustumCorners[2];
            _currentFrustumCorners[2] = temp;

            _ambientOcclusionShader.FrustumCorners = _currentFrustumCorners;
            //Shaders.deferredEnvironmentParameter_FrustumCorners.SetValue(_currentFrustumCorners);
        }

        public void UpdateRenderTargets()
        {
            if(_linearDepthTarget!=null) _linearDepthTarget.Dispose();

            _linearDepthTarget = new RenderTarget2D(_graphics, GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            _ambientOcclusionShader.DepthMap = _linearDepthTarget;
            _ambientOcclusionShader.Resolution = new Vector2( GameSettings.g_ScreenWidth, GameSettings.g_ScreenHeight);
           
        }
    }
}
