// Global keyboard shortcuts. Lives in its own file so the CSP can drop 'unsafe-inline'
// for script-src — the strongest defense against a hostile note injecting <script> into
// the rendered DOM.

// Shift-toets tracker voor WikiLink navigatie
window._shiftPressed = false;
document.addEventListener('keydown', e => { if (e.key === 'Shift') window._shiftPressed = true; });
document.addEventListener('keyup',   e => { if (e.key === 'Shift') window._shiftPressed = false; });
window.isShiftPressed = () => window._shiftPressed;

// Ctrl+S / Ctrl+Enter = opslaan, Escape = annuleren in edit mode; Ctrl+W = tab sluiten
document.addEventListener('keydown', e => {
    if (e.ctrlKey && e.key === 'w') {
        e.preventDefault();
        const tabRef = window.TabDelegate && window.TabDelegate._ref;
        if (tabRef) tabRef.invokeMethodAsync('HandleAction', 'close-active', '');
        return;
    }
    const ref = window.NoteBlockDelegate && window.NoteBlockDelegate._activeRef;
    if (!ref) return;
    if (e.ctrlKey && (e.key === 's' || e.key === 'Enter')) {
        e.preventDefault();
        ref.invokeMethodAsync('HandleNoteAction', 'save', '');
    } else if (e.key === 'Escape') {
        e.preventDefault();
        ref.invokeMethodAsync('HandleNoteAction', 'cancel', '');
    }
}, true);

// Ctrl+V image paste — same capture pattern as Ctrl+S/Escape above.
//
// M2: a clipboard with BOTH an image and text used to double-paste — the editor's
// default handler inserted the text, AND we inserted the image markdown. Now we
// SYNCHRONOUSLY preventDefault during the keydown so the resulting paste event is
// suppressed, then async decide what to insert. If an image is on the clipboard we
// insert the markdown link; otherwise we read clipboard text via the modern
// navigator.clipboard API and insert that, so plain-text paste still works.
document.addEventListener('keydown', async e => {
    if (!(e.ctrlKey && e.key === 'v')) return;
    const ref = window.NoteBlockDelegate && window.NoteBlockDelegate._activeRef;
    if (!ref) return;

    // SYNC: cancel default before any await so the browser's paste event never fires.
    // Other Ctrl+V handlers (tiptap-bridge.js) check e.defaultPrevented and bow out.
    e.preventDefault();
    e.stopPropagation();

    const insertText = (text) => {
        if (!text) return;
        const editorEl = document.querySelector('.note-block.editing .tiptap-editor');
        if (editorEl && window.TipTapBridge) {
            window.TipTapBridge.insertMarkdown(editorEl.id, text);
        } else {
            const ta = document.querySelector('.note-block.editing textarea');
            if (ta) {
                const pos = ta.selectionStart ?? ta.value.length;
                ta.value = ta.value.slice(0, pos) + text + ta.value.slice(pos);
                ta.dispatchEvent(new Event('input'));
            }
        }
    };

    // Async: prefer image. SaveClipboardImageAsync logs internally on failure (M1).
    const relativePath = await ref.invokeMethodAsync('SaveClipboardImageAsync');
    if (relativePath) {
        insertText(`![](${relativePath})`);
        return;
    }

    // No image — manually paste text since we cancelled the default.
    try {
        const text = await navigator.clipboard.readText();
        insertText(text);
    } catch (err) {
        // Clipboard text read can fail if no permission or the clipboard is empty/binary.
        console.error('Clipboard text read failed:', err);
    }
}, true);
