/**
 * 3D场景可视化相关类型定义
 */

import * as THREE from 'three';

/**
 * 设备标注点3D坐标
 */
export interface Position3D {
  x: number;
  y: number;
  z: number;
}

/**
 * 设备标注点旋转角度
 */
export interface Rotation3D {
  x: number;
  y: number;
  z: number;
}

/**
 * 3D设备标注点
 */
export interface DeviceMarker3D {
  id: string;
  deviceId: string; // 关联DeviceContext中的设备ID
  name: string;
  position: Position3D;
  rotation?: Rotation3D;
  modelPath?: string; // 3D模型路径(可选)
  scale?: number; // 缩放比例
  color?: number; // 标注点颜色(十六进制)
}

/**
 * 相机配置
 */
export interface CameraConfig {
  position: [number, number, number];
  lookAt: [number, number, number];
  fov?: number;
  near?: number;
  far?: number;
}

/**
 * 灯光配置
 */
export interface LightConfig {
  type: 'ambient' | 'directional' | 'point' | 'spot';
  color?: number;
  intensity?: number;
  position?: [number, number, number];
  target?: [number, number, number];
}

/**
 * 3D场景配置
 */
export interface SceneConfig {
  areaId: string;
  areaName: string;
  cadFilePath: string; // CAD文件路径
  devices: DeviceMarker3D[];
  camera?: CameraConfig;
  lights?: LightConfig[];
  backgroundColor?: number;
  environmentMapPath?: string;
}

/**
 * 场景构建结果
 */
export interface SceneBuildResult {
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  renderer: THREE.WebGLRenderer;
  controls: any; // OrbitControls实例
}

/**
 * 设备交互事件
 */
export interface DeviceInteractionEvent {
  deviceId: string;
  deviceData: DeviceMarker3D;
  position: THREE.Vector3;
  screenPosition: { x: number; y: number };
}

/**
 * 场景加载状态
 */
export type SceneLoadingState = 'idle' | 'loading' | 'success' | 'error';

/**
 * 场景错误类型
 */
export class SceneError extends Error {
  constructor(
    public code: 'CAD_PARSE_ERROR' | 'SCENE_BUILD_ERROR' | 'RENDER_ERROR' | 'UNKNOWN_ERROR',
    message: string,
    public details?: any
  ) {
    super(message);
    this.name = 'SceneError';
  }
}

/**
 * CAD解析选项
 */
export interface CADParseOptions {
  scale?: number; // 缩放比例
  rotation?: number; // 旋转角度(弧度)
  offset?: Position3D; // 偏移量
  extractLayers?: string[]; // 要提取的图层名称
}