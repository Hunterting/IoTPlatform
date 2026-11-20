import { useEffect, useRef, useState } from 'react';
import Hls from 'hls.js';
import { Play } from 'lucide-react';

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
  /** 覆盖层被点击时可选回调（宫格传入以触发放大；放大 Modal 内不传）。 */
  onOverlayClick?: () => void;
}

/** 判定“直播卡住”的等待阈值（毫秒）：缓冲停顿超过该时长才尝试回到 live edge。 */
const STALL_RECOVER_MS = 5000;

/**
 * HLS 实时流播放器（hls.js 浏览器硬解）。
 * - 浏览器原生支持 HLS 时（Safari）直接用 <video>；
 * - 其余浏览器用 hls.js 挂载 MediaSource（Chrome 走 MSE 硬解，萤石默认推 H264）；
 * - 组件卸载/换源时销毁 hls 实例、解绑 video，避免泄漏与无限重连。
 * - 自动播放被浏览器策略拦截时显示“点击播放”覆盖层，把一次静默失败变成可见的用户手势入口；
 * - 直播缓冲卡住（首帧不动）时在前台且期望播放的前提下尝试 seek 到 live edge 并 startLoad 恢复。
 */
export function HlsPlayer({ url, playing = true, muted = true, className = '', onError, onOverlayClick }: HlsPlayerProps) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const hlsRef = useRef<Hls | null>(null);
  const [error, setError] = useState<string | null>(null);
  /** 反映 <video> 的真实播放状态（由 play/playing/pause/ended 事件驱动），区别于父级入参 playing。 */
  const [isPlaying, setIsPlaying] = useState(false);

  /** 供异步事件回调（stall timer / hls ERROR）读取最新入参，避免闭包过期。 */
  const playingRef = useRef(playing);
  const mutedRef = useRef(muted);
  const errorRef = useRef<string | null>(null);
  playingRef.current = playing;
  mutedRef.current = muted;
  errorRef.current = error;

  /** 由 hls 生命周期 effect 注入的“尝试播放”函数；覆盖层点击时走同一入口。 */
  const attemptPlayRef = useRef<() => void>(() => {});
  /** 卡住恢复定时器需跨 effect 清理，故提升到组件级 ref。 */
  const stallTimerRef = useRef<number | null>(null);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !url) return;

    setError(null);
    setIsPlaying(false);
    let disposed = false;

    /** 清除卡住恢复定时器（幂等）。 */
    const clearStallTimer = () => {
      if (stallTimerRef.current !== null) {
        window.clearTimeout(stallTimerRef.current);
        stallTimerRef.current = null;
      }
    };

    const fail = (msg: string) => {
      if (disposed) return;
      setError(msg);
      onError?.(msg);
    };

    /** 统一播放入口：确保静音状态与入参一致后再 play，失败时同步真实状态用于显示覆盖层。 */
    const attemptPlay = () => {
      if (disposed) return;
      video.muted = mutedRef.current;
      try {
        const result = video.play();
        if (result && typeof (result as Promise<void>).then === 'function') {
          (result as Promise<void>)
            .then(() => { if (!disposed) setIsPlaying(true); })
            // 以 video.paused 为准：重复 play() 导致的前一个 promise 被 AbortError 中断时，
            // 画面可能实际已在播放，不能误判为“被拦截”而弹出覆盖层。
            .catch(() => { if (!disposed) setIsPlaying(!video.paused); });
        }
      } catch {
        if (!disposed) setIsPlaying(!video.paused);
      }
    };
    attemptPlayRef.current = attemptPlay;

    // ── 真实播放状态同步 ──
    const onPlay = () => { if (!disposed) setIsPlaying(true); };
    const onPlaying = () => { if (!disposed) { setIsPlaying(true); clearStallTimer(); } };
    const onPause = () => { if (!disposed) { setIsPlaying(false); clearStallTimer(); } };
    const onEnded = () => { if (!disposed) setIsPlaying(false); };

    // ── 直播卡住恢复 ──
    // timeupdate 有推进即说明首批/后续分片正常渲染，重置定时器；
    // waiting/stalled 持续超过 STALL_RECOVER_MS 才判定为“卡首帧/卡住”。
    const onTimeUpdate = () => { if (!disposed) clearStallTimer(); };
    const onStall = () => {
      // 全局暂停（props.playing=false）或已处于 error 态（网络/媒体错误正在重试）时不恢复，
      // 避免与 ERROR 分支的 startLoad/recoverMediaError 重复拉流。
      if (disposed || !playingRef.current || !!errorRef.current) return;
      clearStallTimer();
      stallTimerRef.current = window.setTimeout(() => {
        stallTimerRef.current = null;
        if (disposed || !playingRef.current || !!errorRef.current) return;
        const hls = hlsRef.current;
        try {
          if (hls && typeof hls.liveSyncPosition === 'number' && Number.isFinite(hls.liveSyncPosition)) {
            // 回到直播边缘：直接解决 back buffer 卡住导致画面停在第一帧的问题。
            video.currentTime = hls.liveSyncPosition;
          }
          if (hls) {
            // 与 NETWORK_ERROR 分支互不重复：该分支会先 clearStallTimer，走到这里说明并非网络错误重试。
            hls.startLoad();
          } else {
            // Safari 原生 HLS：重载 src 触发重新缓冲。
            const src = video.src;
            if (src) { video.src = ''; video.src = src; video.load(); attemptPlay(); }
          }
        } catch {
          // 恢复失败保留现状，fatal error 由 ERROR 分支兜底。
        }
      }, STALL_RECOVER_MS);
    };

    video.addEventListener('play', onPlay);
    video.addEventListener('playing', onPlaying);
    video.addEventListener('pause', onPause);
    video.addEventListener('ended', onEnded);
    video.addEventListener('timeupdate', onTimeUpdate);
    video.addEventListener('waiting', onStall);
    video.addEventListener('stalled', onStall);

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
        if (disposed || !playingRef.current) return;
        // 首帧对齐 live edge（liveSyncPosition 此时可能为 null，需守卫）。
        // liveSyncDurationCount 通常已处理，此处兜底防止初始落在过期位置导致画面不动。
        const edge = hls.liveSyncPosition;
        if (typeof edge === 'number' && Number.isFinite(edge) && edge > 0 && Math.abs(video.currentTime - edge) > 10) {
          try { video.currentTime = edge; } catch { /* 尚未可 seek 时忽略 */ }
        }
        // 自动播放失败不再静默：catch 里同步真实状态，覆盖层随即显示“点击播放”。
        attemptPlay();
      });
      hls.on(Hls.Events.ERROR, (_evt, data) => {
        if (!data.fatal) return;
        clearStallTimer(); // 网络错误由本分支立即 startLoad，避免与 stall 恢复重复
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
      attemptPlayRef.current = () => {};
      clearStallTimer();
      video.removeEventListener('play', onPlay);
      video.removeEventListener('playing', onPlaying);
      video.removeEventListener('pause', onPause);
      video.removeEventListener('ended', onEnded);
      video.removeEventListener('timeupdate', onTimeUpdate);
      video.removeEventListener('waiting', onStall);
      video.removeEventListener('stalled', onStall);
      if (hlsRef.current) {
        hlsRef.current.destroy();
        hlsRef.current = null;
      }
      video.removeAttribute('src');
      video.load();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [url]);

  // 播放/暂停（父级全局控制）
  // 注意：autoplay 是否被拦截以 video 真实事件为准（isPlaying），这里只负责发出意图。
  useEffect(() => {
    const video = videoRef.current;
    if (!video || !url) return;
    if (playing) attemptPlayRef.current();
    else video.pause();
  }, [playing, url]);

  // 静音
  useEffect(() => {
    const video = videoRef.current;
    if (video) video.muted = muted;
  }, [muted]);

  /**
   * 覆盖层显示条件：父级期望播放 === true，且视频实际未播放，且没有致命错误。
   * 父级全局暂停（playing=false）时不显示，避免把“用户主动暂停”误提示为“点击播放”。
   */
  const showPlayOverlay = playing && !isPlaying && !error;

  return (
    <div className={`relative w-full h-full bg-black ${className}`}>
      <video
        ref={videoRef}
        className="w-full h-full object-cover"
        muted={muted}
        playsInline
        controls={false}
      />
      {showPlayOverlay && (
        <button
          type="button"
          aria-label="点击播放"
          onClick={(e) => {
            // 阻止冒泡到宫格卡片的通用“点击放大”处理器，避免重复触发；
            // 但通过显式回调让宫格场景仍能放大（放大 Modal 内不传该回调，只播放）。
            e.stopPropagation();
            attemptPlayRef.current();
            onOverlayClick?.();
          }}
          className="absolute inset-0 z-10 flex flex-col items-center justify-center gap-2 group cursor-pointer"
          style={{ background: 'rgba(0,0,0,0.45)' }}
        >
          <span
            className="flex items-center justify-center w-12 h-12 rounded-full bg-white/20 border-2 border-white/70 shadow-lg transition-transform group-hover:scale-110"
          >
            <Play className="w-6 h-6 text-white" fill="white" strokeWidth={1.5} />
          </span>
          <span className="text-xs text-white/90 font-medium" style={{ textShadow: '0 1px 3px #000' }}>
            点击播放
          </span>
        </button>
      )}
      {error && (
        <div className="absolute inset-0 z-20 flex items-center justify-center px-2 text-center"
          style={{ background: 'rgba(0,0,0,0.75)' }}>
          <span className="text-[10px]" style={{ color: '#ffaa00' }}>{error}</span>
        </div>
      )}
    </div>
  );
}
