import { useEffect, useRef, useState } from 'react';
import Hls from 'hls.js';

export interface HlsPlayerProps {
  /** 萤石 v2/live/address/get 返回的 HLS(m3u8) 地址 */
  url: string;
  playing?: boolean;
  muted?: boolean;
  className?: string;
  onError?: (message: string) => void;
  /**
   * 兼容旧接口的可选回调。hls.js 硬解内核不解析 codec 元信息，故不再触发；
   * 保留字段声明仅为类型兼容，现有调用方（MonitoringPage 两处）无需改动。
   */
  onVideoInfo?: (info: { codec?: string; width?: number; height?: number }) => void;
}

/**
 * HLS 实时流播放器（hls.js 浏览器硬解）。
 * - 浏览器原生支持 HLS 时（Safari）直接用 <video>；
 * - 其余浏览器用 hls.js 挂载 MediaSource（Chrome 走 MSE 硬解，萤石默认推 H264）；
 * - 组件卸载/换源时销毁 hls 实例、解绑 video，避免泄漏与无限重连。
 */
export function HlsPlayer({ url, playing = true, muted = true, className = '', onError }: HlsPlayerProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const hlsRef = useRef<Hls | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !url) return;

    setError(null);
    let disposed = false;

    const fail = (msg: string) => {
      if (disposed) return;
      setError(msg);
      onError?.(msg);
    };

    if (video.canPlayType('application/vnd.apple.mpegurl')) {
      // Safari 原生 HLS
      video.src = url;
    } else if (Hls.isSupported()) {
      const hls = new Hls({
        enableWorker: true,
        lowLatencyMode: true,
        // 直播流：限制缓冲，降低延迟
        maxBufferLength: 10,
        maxMaxBufferLength: 20,
        liveSyncDurationCount: 3,
      });
      hlsRef.current = hls;
      hls.loadSource(url);
      hls.attachMedia(video);
      hls.on(Hls.Events.MANIFEST_PARSED, () => {
        if (!disposed && playing) video.play().catch(() => { /* 自动播放被拦截，忽略 */ });
      });
      hls.on(Hls.Events.ERROR, (_evt, data) => {
        if (!data.fatal) return;
        switch (data.type) {
          case Hls.ErrorTypes.NETWORK_ERROR:
            hls.startLoad();
            fail('直播网络异常，正在重试');
            break;
          case Hls.ErrorTypes.MEDIA_ERROR:
            hls.recoverMediaError();
            break;
          default:
            fail('直播流加载失败：' + (data.details || '未知错误'));
            hls.destroy();
            hlsRef.current = null;
            break;
        }
      });
    } else {
      fail('当前浏览器不支持 HLS 播放');
    }

    return () => {
      disposed = true;
      if (hlsRef.current) {
        hlsRef.current.destroy();
        hlsRef.current = null;
      }
      video.removeAttribute('src');
      video.load();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url]);

  // 播放/暂停
  useEffect(() => {
    const video = videoRef.current;
    if (!video || !url) return;
    if (playing) video.play().catch(() => { /* 忽略自动播放限制 */ });
    else video.pause();
  }, [playing, url]);

  // 静音
  useEffect(() => {
    const video = videoRef.current;
    if (video) video.muted = muted;
  }, [muted]);

  return (
    <div className={`relative w-full h-full bg-black ${className}`}>
      <video
        ref={videoRef}
        className="w-full h-full object-cover"
        muted={muted}
        playsInline
        controls={false}
      />
      {error && (
        <div className="absolute inset-0 flex items-center justify-center px-2 text-center"
          style={{ background: 'rgba(0,0,0,0.75)' }}>
          <span className="text-[10px]" style={{ color: '#ffaa00' }}>{error}</span>
        </div>
      )}
    </div>
  );
}
