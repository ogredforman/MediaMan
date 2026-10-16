import { clampVolume, iconButtonClass, volumeStep } from './controls';
import { VolumeIcon } from './playerIcons';

interface VolumeControlProps {
  /** 0–100. */
  volume: number;
  muted: boolean;
  onToggleMute: () => void;
  onSetVolume: (volume: number) => void;
}

/** The mute button with a volume slider that opens on hover or focus; the wheel over either adjusts the volume. */
export function VolumeControl({ volume, muted, onToggleMute, onSetVolume }: VolumeControlProps) {
  return (
    <div
      className="group flex items-center"
      onWheel={(e) => {
        if (e.deltaY !== 0) onSetVolume(clampVolume(volume + (e.deltaY < 0 ? volumeStep : -volumeStep)));
      }}
    >
      <button type="button" className={iconButtonClass} aria-label={muted ? 'Unmute' : 'Mute'} title={muted ? 'Unmute (M)' : 'Mute (M)'} onClick={onToggleMute}>
        <VolumeIcon level={muted ? 0 : volume} />
      </button>
      <div className="w-0 overflow-hidden transition-[width] duration-(--motion-normal) group-focus-within:w-28 group-hover:w-28">
        <input
          type="range"
          min={0}
          max={100}
          step={1}
          value={volume}
          aria-label="Volume"
          aria-valuetext={muted ? `${String(volume)}%, muted` : `${String(volume)}%`}
          className="mx-2 w-24 cursor-pointer accent-accent"
          onChange={(e) => {
            onSetVolume(clampVolume(Number(e.target.value)));
          }}
        />
      </div>
    </div>
  );
}
