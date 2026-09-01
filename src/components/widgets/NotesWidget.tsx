import { useEffect, useRef, useState } from "react";
import type { NotesSettings } from "../../lib/widgets";

interface NotesWidgetProps {
  settings?: NotesSettings;
  edit: boolean;
  onSave: (s: NotesSettings) => void;
}

/**
 * Notes widget: a private scratchpad that autosaves to the widget's settings
 * (persisted, synced to every island window). The textarea appears in edit
 * mode (⚙); closed mode shows the saved text as plain, wrapped text.
 */
export function NotesWidget({ settings, edit, onSave }: NotesWidgetProps) {
  const [text, setText] = useState(settings?.text ?? "");
  const timer = useRef<number | null>(null);

  // Adopt external changes (cross-window sync / reset-to-default).
  useEffect(() => {
    setText(settings?.text ?? "");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settings?.text]);

  // Clear any pending autosave when unmounting.
  useEffect(
    () => () => {
      if (timer.current !== null) window.clearTimeout(timer.current);
    },
    [],
  );

  const change = (v: string) => {
    setText(v);
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => onSave({ text: v }), 400);
  };

  if (edit) {
    return (
      <div className="widget-edit">
        <textarea
          className="notes-input"
          value={text}
          placeholder="Write a note…"
          rows={4}
          onChange={(e) => change(e.target.value)}
        />
      </div>
    );
  }

  return (
    <p className="notes-view">
      {text.length > 0 ? text : "No note yet — open the ⚙ on this card to write one."}
    </p>
  );
}