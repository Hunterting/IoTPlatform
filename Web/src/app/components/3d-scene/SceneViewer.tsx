/**
 * 3D场景查看器核心组件
 * 负责渲染3D场景、处理用户交互、设备标注等
 */

import { useEffect, useRef, useState, useCallback } from 'react';
import { SceneBuilderService } from '@/app/services/scene/SceneBuilderService';
import { SceneConfig, SceneLoadingState, SceneError, DeviceInteractionEvent } from '@/app/types/3d-scene';
import { Cpu, AlertCircle } from 'lucide-react';

interface SceneViewerProps {
  sceneConfig: SceneConfig;
  onDeviceClick?: (deviceId: string, deviceData: any) => void;
  onHoverDevice?: (deviceId: string | null, deviceData?: any) => void;
  className?: string;
}

export function SceneViewer({
  sceneConfig,
  onDeviceClick,
  onHoverDevice,
  className = '',
}: SceneViewerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const sceneRef = useRef<any>(null);
  const animationFrameRef = useRef<number>(0);
  
  const [loadingState, setLoadingState] = useState<SceneLoadingState>('idle');
  const [error, setError] = useState<string | null>(null);
  const [hoveredDeviceId, setHoveredDeviceId] = useState<string | null>(null);

  // 处理窗口大小变化
  const handleResize = useCallback(() => {
    if (!sceneRef.current) return;
    
    const { camera, renderer } = sceneRef.current;
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  }, []);

  // 处理鼠标移动(设备悬停检测)
  const handleMouseMove = useCallback((event: MouseEvent) => {
    if (!sceneRef.current || !containerRef.current) return;
    
    const { scene, camera, renderer } = sceneRef.current;
    const rect = renderer.domElement.getBoundingClientRect();
    
    // 计算鼠标在归一化设备坐标(NDC)中的位置
    const mouse = {
      x: ((event.clientX - rect.left) / rect.width) * 2 - 1,
      y: -((event.clientY - rect.top) / rect.height) * 2 + 1,
    };

    // 创建射线检测器
    const raycaster = new (window as any).THREE.Raycaster();
    raycaster.setFromCamera(mouse, camera);

    // 检测交叉的物体
    const intersects = raycaster.intersectObjects(scene.children, true);

    // 查找第一个设备标注
    const deviceMarker = intersects.find(
      intersect => intersect.object.parent?.userData?.isDeviceMarker ||
                  intersect.object.userData?.isDeviceMarker
    );

    if (deviceMarker) {
      const markerGroup = deviceMarker.object.parent?.userData?.isDeviceMarker 
        ? deviceMarker.object.parent 
        : deviceMarker.object;
      
      const deviceId = markerGroup.userData.deviceId;
      const deviceData = markerGroup.userData.deviceData;
      
      if (hoveredDeviceId !== deviceId) {
        setHoveredDeviceId(deviceId);
        onHoverDevice?.(deviceId, deviceData);
      }
      
      // 更新光标样式
      renderer.domElement.style.cursor = 'pointer';
    } else {
      if (hoveredDeviceId !== null) {
        setHoveredDeviceId(null);
        onHoverDevice?.(null);
      }
      
      renderer.domElement.style.cursor = 'default';
    }
  }, [hoveredDeviceId, onHoverDevice]);

  // 处理点击事件(设备选择)
  const handleClick = useCallback((event: MouseEvent) => {
    if (!sceneRef.current || !containerRef.current) return;
    
    const { scene, camera, renderer } = sceneRef.current;
    const rect = renderer.domElement.getBoundingClientRect();
    
    const mouse = {
      x: ((event.clientX - rect.left) / rect.width) * 2 - 1,
      y: -((event.clientY - rect.top) / rect.height) * 2 + 1,
    };

    const raycaster = new (window as any).THREE.Raycaster();
    raycaster.setFromCamera(mouse, camera);

    const intersects = raycaster.intersectObjects(scene.children, true);

    const deviceMarker = intersects.find(
      intersect => intersect.object.parent?.userData?.isDeviceMarker ||
                  intersect.object.userData?.isDeviceMarker
    );

    if (deviceMarker) {
      const markerGroup = deviceMarker.object.parent?.userData?.isDeviceMarker 
        ? deviceMarker.object.parent 
        : deviceMarker.object;
      
      const deviceId = markerGroup.userData.deviceId;
      const deviceData = markerGroup.userData.deviceData;
      
      onDeviceClick?.(deviceId, deviceData);
    }
  }, [onDeviceClick]);

  // 初始化场景
  useEffect(() => {
    if (!containerRef.current) return;

    let sceneResult: any = null;

    const initScene = async () => {
      try {
        setLoadingState('loading');
        setError(null);

        // 构建场景
        sceneResult = await SceneBuilderService.buildScene(sceneConfig);
        sceneRef.current = sceneResult;

        // 将渲染器添加到DOM
        containerRef.current.appendChild(sceneResult.renderer.domElement);

        // 添加事件监听器
        sceneResult.renderer.domElement.addEventListener('mousemove', handleMouseMove);
        sceneResult.renderer.domElement.addEventListener('click', handleClick);
        window.addEventListener('resize', handleResize);

        // 开始动画循环
        const startTime = Date.now();
        
        const animate = () => {
          animationFrameRef.current = requestAnimationFrame(animate);
          
          const time = (Date.now() - startTime) / 1000;
          
          // 更新控制器
          sceneResult.controls.update();
          
          // 更新设备标注动画
          SceneBuilderService.updateDeviceMarkersAnimation(sceneResult.scene, time);
          
          // 渲染场景
          sceneResult.renderer.render(sceneResult.scene, sceneResult.camera);
        };

        animate();
        setLoadingState('success');

      } catch (err) {
        console.error('场景初始化失败:', err);
        setError(err instanceof Error ? err.message : '场景加载失败');
        setLoadingState('error');
      }
    };

    initScene();

    // 清理函数
    return () => {
      if (animationFrameRef.current) {
        cancelAnimationFrame(animationFrameRef.current);
      }
      
      if (sceneResult) {
        sceneResult.renderer.domElement.removeEventListener('mousemove', handleMouseMove);
        sceneResult.renderer.domElement.removeEventListener('click', handleClick);
        window.removeEventListener('resize', handleResize);
        
        SceneBuilderService.disposeScene(sceneResult.scene, sceneResult.renderer);
        
        if (containerRef.current && sceneResult.renderer.domElement) {
          containerRef.current.removeChild(sceneResult.renderer.domElement);
        }
      }
    };
  }, [sceneConfig, handleMouseMove, handleClick, handleResize]);

  // 加载状态
  if (loadingState === 'loading') {
    return (
      <div className={`flex items-center justify-center h-full bg-gray-900/50 ${className}`}>
        <div className="text-center">
          <div className="w-16 h-16 mx-auto mb-4 border-4 border-cyan-500/30 border-t-cyan-400 rounded-full animate-spin" />
          <p className="text-cyan-400 text-sm">3D场景加载中...</p>
          <p className="text-gray-500 text-xs mt-2">正在解析CAD文件并构建场景</p>
        </div>
      </div>
    );
  }

  // 错误状态
  if (loadingState === 'error') {
    return (
      <div className={`flex items-center justify-center h-full bg-gray-900/50 ${className}`}>
        <div className="text-center max-w-md">
          <AlertCircle className="w-16 h-16 mx-auto mb-4 text-red-400" />
          <p className="text-red-400 text-lg mb-2">场景加载失败</p>
          <p className="text-gray-400 text-sm mb-4">{error}</p>
          <button
            onClick={() => {
              setLoadingState('idle');
              setError(null);
            }}
            className="px-4 py-2 bg-cyan-500 text-white rounded hover:bg-cyan-600 transition-colors text-sm"
          >
            重试
          </button>
        </div>
      </div>
    );
  }

  // 成功状态 - 渲染场景
  return (
    <div className={`relative w-full h-full ${className}`}>
      {/* 场景容器 */}
      <div ref={containerRef} className="w-full h-full" />
      
      {/* 加载完成提示 */}
      {loadingState === 'success' && (
        <div className="absolute bottom-4 left-4 bg-black/60 backdrop-blur-sm rounded-lg px-3 py-2">
          <div className="flex items-center gap-2">
            <Cpu className="w-4 h-4 text-cyan-400" />
            <span className="text-xs text-gray-300">
              {sceneConfig.devices.length} 个设备标注
            </span>
          </div>
        </div>
      )}
    </div>
  );
}