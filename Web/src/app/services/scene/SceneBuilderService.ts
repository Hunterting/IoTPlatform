/**
 * 3D场景构建服务
 * 负责构建和管理3D场景的创建、相机配置、灯光设置等
 */

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { CADParserService } from '../cad/CADParserService';
import { SceneConfig, SceneBuildResult, SceneError } from '@/app/types/3d-scene';

export class SceneBuilderService {
  /**
   * 构建3D场景
   * @param config 场景配置
   * @returns 场景构建结果
   */
  static async buildScene(config: SceneConfig): Promise<SceneBuildResult> {
    try {
      // 创建场景
      const scene = this.createScene(config);
      
      // 添加灯光
      this.setupLighting(scene, config.lights);
      
      // 加载CAD文件
      const cadGroup = await this.loadCADFile(config);
      scene.add(cadGroup);
      
      // 添加设备标注
      const deviceMarkers = this.createDeviceMarkers(config);
      deviceMarkers.forEach(marker => scene.add(marker));
      
      // 创建相机
      const camera = this.createCamera(config);
      
      // 创建渲染器
      const renderer = this.createRenderer();
      
      // 创建轨道控制器
      const controls = this.createControls(camera, renderer.domElement);
      
      // 自动调整相机位置以包含所有物体
      this.fitCameraToScene(scene, camera, controls);

      return {
        scene,
        camera,
        renderer,
        controls,
      };
    } catch (error) {
      if (error instanceof SceneError) {
        throw error;
      }
      throw new SceneError(
        'SCENE_BUILD_ERROR',
        `场景构建失败: ${error instanceof Error ? error.message : '未知错误'}`,
        error
      );
    }
  }

  /**
   * 创建基础场景
   */
  private static createScene(config: SceneConfig): THREE.Scene {
    const scene = new THREE.Scene();
    
    // 设置背景色
    const bgColor = config.backgroundColor || 0x0a1628;
    scene.background = new THREE.Color(bgColor);
    
    // 添加雾效增强深度感
    scene.fog = new THREE.Fog(bgColor, 20, 50);
    
    return scene;
  }

  /**
   * 设置灯光
   */
  private static setupLighting(
    scene: THREE.Scene,
    lights?: SceneConfig['lights']
  ): void {
    // 如果没有提供灯光配置,使用默认灯光
    if (!lights || lights.length === 0) {
      // 环境光
      const ambientLight = new THREE.AmbientLight(0xffffff, 0.6);
      scene.add(ambientLight);
      
      // 主方向光
      const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8);
      directionalLight.position.set(10, 20, 10);
      directionalLight.castShadow = true;
      directionalLight.shadow.mapSize.width = 2048;
      directionalLight.shadow.mapSize.height = 2048;
      scene.add(directionalLight);
      
      // 补光
      const fillLight = new THREE.DirectionalLight(0x00c3ff, 0.3);
      fillLight.position.set(-10, 10, -10);
      scene.add(fillLight);
      
      return;
    }
    
    // 使用自定义灯光配置
    lights.forEach(lightConfig => {
      let light: THREE.Light;
      
      switch (lightConfig.type) {
        case 'ambient':
          light = new THREE.AmbientLight(
            lightConfig.color || 0xffffff,
            lightConfig.intensity || 1
          );
          break;
          
        case 'directional':
          light = new THREE.DirectionalLight(
            lightConfig.color || 0xffffff,
            lightConfig.intensity || 1
          );
          if (lightConfig.position) {
            (light as THREE.DirectionalLight).position.set(
              lightConfig.position[0],
              lightConfig.position[1],
              lightConfig.position[2]
            );
          }
          if (lightConfig.target) {
            (light as THREE.DirectionalLight).target.position.set(
              lightConfig.target[0],
              lightConfig.target[1],
              lightConfig.target[2]
            );
          }
          break;
          
        case 'point':
          light = new THREE.PointLight(
            lightConfig.color || 0xffffff,
            lightConfig.intensity || 1,
            10,
            2
          );
          if (lightConfig.position) {
            (light as THREE.PointLight).position.set(
              lightConfig.position[0],
              lightConfig.position[1],
              lightConfig.position[2]
            );
          }
          break;
          
        case 'spot':
          light = new THREE.SpotLight(
            lightConfig.color || 0xffffff,
            lightConfig.intensity || 1
          );
          if (lightConfig.position) {
            (light as THREE.SpotLight).position.set(
              lightConfig.position[0],
              lightConfig.position[1],
              lightConfig.position[2]
            );
          }
          (light as THREE.SpotLight).angle = Math.PI / 6;
          (light as THREE.SpotLight).penumbra = 0.2;
          break;
          
        default:
          return;
      }
      
      scene.add(light);
    });
  }

  /**
   * 加载CAD文件
   */
  private static async loadCADFile(config: SceneConfig): Promise<THREE.Group> {
    try {
      return await CADParserService.parseCADFile(config.cadFilePath);
    } catch (error) {
      console.error('CAD文件加载失败,使用降级方案:', error);
      // 返回一个空的组
      return new THREE.Group();
    }
  }

  /**
   * 创建设备标注
   */
  private static createDeviceMarkers(config: SceneConfig): THREE.Group[] {
    return config.devices.map(deviceConfig => {
      return this.createDeviceMarker(deviceConfig);
    });
  }

  /**
   * 创建单个设备标注
   */
  static createDeviceMarker(deviceConfig: SceneConfig['devices'][0]): THREE.Group {
    const group = new THREE.Group();
    
    // 设置位置
    group.position.set(
      deviceConfig.position.x,
      deviceConfig.position.y,
      deviceConfig.position.z
    );
    
    // 设置旋转
    if (deviceConfig.rotation) {
      group.rotation.set(
        deviceConfig.rotation.x,
        deviceConfig.rotation.y,
        deviceConfig.rotation.z
      );
    }
    
    // 设置缩放
    const scale = deviceConfig.scale || 1;
    group.scale.set(scale, scale, scale);
    
    // 设置颜色
    const color = deviceConfig.color || 0x00c3ff;
    
    // 创建设备主体(圆柱体表示)
    const cylinderGeometry = new THREE.CylinderGeometry(0.3, 0.3, 0.6, 32);
    const cylinderMaterial = new THREE.MeshPhongMaterial({
      color: color,
      emissive: color,
      emissiveIntensity: 0.3,
      shininess: 100,
    });
    const cylinder = new THREE.Mesh(cylinderGeometry, cylinderMaterial);
    cylinder.position.y = 0.3;
    cylinder.castShadow = true;
    group.add(cylinder);
    
    // 创建顶部标记(球体)
    const sphereGeometry = new THREE.SphereGeometry(0.2, 32, 32);
    const sphereMaterial = new THREE.MeshPhongMaterial({
      color: 0xffffff,
      emissive: 0xffffff,
      emissiveIntensity: 0.5,
    });
    const sphere = new THREE.Mesh(sphereGeometry, sphereMaterial);
    sphere.position.y = 0.7;
    group.add(sphere);
    
    // 创建底部光圈(环)
    const ringGeometry = new THREE.RingGeometry(0.4, 0.6, 32);
    const ringMaterial = new THREE.MeshBasicMaterial({
      color: color,
      transparent: true,
      opacity: 0.5,
      side: THREE.DoubleSide,
    });
    const ring = new THREE.Mesh(ringGeometry, ringMaterial);
    ring.rotation.x = -Math.PI / 2;
    ring.position.y = 0.02;
    group.add(ring);
    
    // 创建脉冲光圈动画
    const pulseRingGeometry = new THREE.RingGeometry(0.5, 0.55, 32);
    const pulseRingMaterial = new THREE.MeshBasicMaterial({
      color: color,
      transparent: true,
      opacity: 0.3,
      side: THREE.DoubleSide,
    });
    const pulseRing = new THREE.Mesh(pulseRingGeometry, pulseRingMaterial);
    pulseRing.rotation.x = -Math.PI / 2;
    pulseRing.position.y = 0.01;
    pulseRing.userData = { isPulseRing: true };
    group.add(pulseRing);
    
    // 存储设备数据以便交互
    group.userData = {
      isDeviceMarker: true,
      deviceId: deviceConfig.deviceId,
      deviceData: deviceConfig,
      pulseRing: pulseRing,
    };
    
    return group;
  }

  /**
   * 创建相机
   */
  private static createCamera(config: SceneConfig): THREE.PerspectiveCamera {
    const camera = new THREE.PerspectiveCamera(
      config.camera?.fov || 60,
      window.innerWidth / window.innerHeight,
      config.camera?.near || 0.1,
      config.camera?.far || 1000
    );
    
    if (config.camera) {
      camera.position.set(...config.camera.position);
    } else {
      // 默认相机位置
      camera.position.set(15, 12, 15);
    }
    
    return camera;
  }

  /**
   * 创建渲染器
   */
  private static createRenderer(): THREE.WebGLRenderer {
    const renderer = new THREE.WebGLRenderer({
      antialias: true,
      alpha: true,
      powerPreference: 'high-performance',
    });
    
    renderer.setSize(window.innerWidth, window.innerHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.shadowMap.enabled = true;
    renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    renderer.outputColorSpace = THREE.SRGBColorSpace;
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1;
    
    return renderer;
  }

  /**
   * 创建轨道控制器
   */
  private static createControls(
    camera: THREE.PerspectiveCamera,
    domElement: HTMLElement
  ): OrbitControls {
    const controls = new OrbitControls(camera, domElement);
    
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.minDistance = 2;
    controls.maxDistance = 50;
    controls.maxPolarAngle = Math.PI / 2 - 0.1; // 防止相机进入地下
    controls.minPolarAngle = 0.1;
    controls.enablePan = true;
    controls.panSpeed = 1;
    controls.rotateSpeed = 1;
    controls.zoomSpeed = 1.2;
    
    return controls;
  }

  /**
   * 自动调整相机位置以包含所有物体
   */
  private static fitCameraToScene(
    scene: THREE.Scene,
    camera: THREE.PerspectiveCamera,
    controls: OrbitControls
  ): void {
    const box = new THREE.Box3().setFromObject(scene);
    const center = box.getCenter(new THREE.Vector3());
    const size = box.getSize(new THREE.Vector3());
    
    const maxDim = Math.max(size.x, size.y, size.z);
    const fov = camera.fov * (Math.PI / 180);
    let cameraZ = Math.abs(maxDim / 2 * Math.tan(fov * 2));
    
    cameraZ *= 2; // 稍微拉远一点
    
    const cameraPos = new THREE.Vector3(
      center.x + cameraZ * 0.707,
      center.y + cameraZ * 0.707,
      center.z + cameraZ * 0.707
    );
    
    camera.position.copy(cameraPos);
    camera.lookAt(center);
    controls.target.copy(center);
    controls.update();
  }

  /**
   * 更新设备标注动画
   */
  static updateDeviceMarkersAnimation(
    scene: THREE.Scene,
    time: number
  ): void {
    scene.traverse((object) => {
      if (object instanceof THREE.Group && object.userData.isDeviceMarker) {
        const pulseRing = object.userData.pulseRing;
        if (pulseRing && pulseRing.material) {
          const material = pulseRing.material as THREE.MeshBasicMaterial;
          const scale = 1 + Math.sin(time * 3) * 0.3;
          material.opacity = 0.3 - Math.sin(time * 3) * 0.15;
          pulseRing.scale.set(scale, scale, 1);
        }
      }
    });
  }

  /**
   * 释放场景资源
   */
  static disposeScene(
    scene: THREE.Scene,
    renderer: THREE.WebGLRenderer
  ): void {
    // 释放几何体和材质
    scene.traverse((object) => {
      if (object instanceof THREE.Mesh) {
        object.geometry.dispose();
        if (Array.isArray(object.material)) {
          object.material.forEach(m => m.dispose());
        } else {
          object.material.dispose();
        }
      }
    });
    
    // 释放渲染器
    renderer.dispose();
  }
}