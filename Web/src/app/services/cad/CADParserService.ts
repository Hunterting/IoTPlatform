/**
 * CAD文件解析服务
 * 用于解析DXF格式的CAD文件并转换为3D场景对象
 */

import * as THREE from 'three';
import { DeviceMarker3D, CADParseOptions, SceneError } from '@/app/types/3d-scene';

export class CADParserService {
  /**
   * 解析CAD文件并提取3D几何体
   * @param cadFileUrl CAD文件URL
   * @param options 解析选项
   * @returns THREE.Group CAD文件转换为的3D对象
   */
  static async parseCADFile(
    cadFileUrl: string,
    options: CADParseOptions = {}
  ): Promise<THREE.Group> {
    try {
      const {
        scale = 1,
        rotation = 0,
        offset = { x: 0, y: 0, z: 0 },
      } = options;

      // 尝试加载CAD文件
      // 注意: 实际项目中需要使用three-dxf或其他CAD解析库
      // 这里提供简化实现作为示例
      
      // 检查文件扩展名
      const fileExtension = cadFileUrl.toLowerCase().split('.').pop();
      
      if (fileExtension === 'dxf') {
        return await this.parseDXFFile(cadFileUrl, { scale, rotation, offset });
      } else if (fileExtension === 'dwg') {
        throw new SceneError(
          'CAD_PARSE_ERROR',
          'DWG格式暂不支持,请转换为DXF格式'
        );
      } else {
        throw new SceneError(
          'CAD_PARSE_ERROR',
          `不支持的CAD文件格式: ${fileExtension}`
        );
      }
    } catch (error) {
      if (error instanceof SceneError) {
        throw error;
      }
      throw new SceneError(
        'CAD_PARSE_ERROR',
        `CAD文件解析失败: ${error instanceof Error ? error.message : '未知错误'}`,
        error
      );
    }
  }

  /**
   * 解析DXF文件
   * @param dxfUrl DXF文件URL
   * @param options 解析选项
   * @returns THREE.Group 3D对象
   */
  private static async parseDXFFile(
    dxfUrl: string,
    options: { scale: number; rotation: number; offset: { x: number; y: number; z: number } }
  ): Promise<THREE.Group> {
    // 实际实现需要使用three-dxf库
    // 这里提供一个基础的示例实现
    
    const group = new THREE.Group();
    
    try {
      // 尝试使用three-dxf解析(需要先导入)
      // const { Viewer } = await import('three-dxf');
      // const viewer = new Viewer(group, dxfUrl, options);
      // await viewer.getScene();
      
      // 临时实现: 创建一个基础平面作为CAD图纸占位
      // 在实际项目中,这里会解析真实的DXF文件内容
      
      const planeGeometry = new THREE.PlaneGeometry(20, 20);
      const planeMaterial = new THREE.MeshBasicMaterial({
        color: 0x00c3ff,
        transparent: true,
        opacity: 0.1,
        side: THREE.DoubleSide,
        wireframe: true,
      });
      const plane = new THREE.Mesh(planeGeometry, planeMaterial);
      plane.rotation.x = -Math.PI / 2;
      plane.position.y = -0.25;
      group.add(plane);

      // 添加网格线
      const gridHelper = new THREE.GridHelper(20, 20, 0x00c3ff, 0x0066aa);
      gridHelper.material.opacity = 0.3;
      gridHelper.material.transparent = true;
      group.add(gridHelper);

      // 添加墙壁示例(模拟CAD中的墙体)
      this.createSampleWalls(group);

    } catch (error) {
      console.error('DXF解析失败,使用降级方案:', error);
      
      // 降级方案: 使用基础几何体
      this.createFallbackScene(group);
    }

    // 应用变换
    group.scale.set(options.scale, options.scale, options.scale);
    group.rotation.y = options.rotation;
    group.position.set(options.offset.x, options.offset.y, options.offset.z);

    return group;
  }

  /**
   * 创建示例墙壁(模拟CAD图纸内容)
   */
  private static createSampleWalls(group: THREE.Group): void {
    const wallMaterial = new THREE.MeshBasicMaterial({
      color: 0x1a3a5c,
      transparent: true,
      opacity: 0.5,
    });

    // 创建四面墙壁
    const walls = [
      { x: 0, z: -5, w: 10, h: 3, d: 0.2 }, // 后墙
      { x: 0, z: 5, w: 10, h: 3, d: 0.2 },  // 前墙
      { x: -5, z: 0, w: 0.2, h: 3, d: 10 }, // 左墙
      { x: 5, z: 0, w: 0.2, h: 3, d: 10 },  // 右墙
    ];

    walls.forEach(wall => {
      const geometry = new THREE.BoxGeometry(wall.w, wall.h, wall.d);
      const mesh = new THREE.Mesh(geometry, wallMaterial);
      mesh.position.set(wall.x, wall.h / 2, wall.z);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      group.add(mesh);
    });
  }

  /**
   * 创建降级场景(当CAD解析失败时使用)
   */
  private static createFallbackScene(group: THREE.Group): void {
    const floorGeometry = new THREE.PlaneGeometry(10, 10);
    const floorMaterial = new THREE.MeshStandardMaterial({
      color: 0x1a2a3a,
      roughness: 0.8,
      metalness: 0.2,
    });
    const floor = new THREE.Mesh(floorGeometry, floorMaterial);
    floor.rotation.x = -Math.PI / 2;
    floor.receiveShadow = true;
    group.add(floor);

    const gridHelper = new THREE.GridHelper(10, 10, 0x00c3ff, 0x004466);
    gridHelper.material.opacity = 0.4;
    gridHelper.material.transparent = true;
    group.add(gridHelper);
  }

  /**
   * 提取CAD文件中的设备位置标注
   * @param cadGroup CAD 3D对象
   * @returns 设备位置标注数组
   */
  static extractDeviceMarkers(cadGroup: THREE.Group): DeviceMarker3D[] {
    const markers: DeviceMarker3D[] = [];

    // 在实际实现中,这里会遍历CAD文件中的特定图层或实体
    // 提取包含设备信息的标注点
    
    // 遍历CAD对象的所有子对象
    cadGroup.traverse((child) => {
      if (child instanceof THREE.Mesh) {
        const userData = child.userData;
        
        // 检查是否是设备标注
        if (userData?.isDeviceMarker && userData?.deviceId) {
          markers.push({
            id: userData.markerId || `marker-${Date.now()}`,
            deviceId: userData.deviceId,
            name: userData.deviceName || '未知设备',
            position: {
              x: child.position.x,
              y: child.position.y,
              z: child.position.z,
            },
            rotation: userData.rotation || { x: 0, y: 0, z: 0 },
            color: userData.color || 0x00c3ff,
          });
        }
      }
    });

    return markers;
  }

  /**
   * 验证CAD文件是否有效
   * @param fileUrl CAD文件URL
   * @returns 是否有效
   */
  static async validateCADFile(fileUrl: string): Promise<boolean> {
    try {
      // 检查文件是否存在
      const response = await fetch(fileUrl, { method: 'HEAD' });
      if (!response.ok) {
        return false;
      }

      // 检查文件扩展名
      const extension = fileUrl.toLowerCase().split('.').pop();
      return ['dxf', 'dwg'].includes(extension || '');
    } catch {
      return false;
    }
  }

  /**
   * 获取CAD文件元数据
   * @param fileUrl CAD文件URL
   * @returns 元数据对象
   */
  static async getCADMetadata(fileUrl: string): Promise<{
    size: number;
    lastModified: string;
    type: string;
  } | null> {
    try {
      const response = await fetch(fileUrl, { method: 'HEAD' });
      if (!response.ok) {
        return null;
      }

      return {
        size: parseInt(response.headers.get('Content-Length') || '0', 10),
        lastModified: response.headers.get('Last-Modified') || '',
        type: response.headers.get('Content-Type') || '',
      };
    } catch {
      return null;
    }
  }
}