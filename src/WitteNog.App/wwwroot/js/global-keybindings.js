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

// Ctrl+V image paste — same capture pattern as Ctrl+S/Escape above
document.addEventListener('keydown', async e => {
    if (!(e.ctrlKey && e.key === 'v')) return;
    const ref = window.NoteBlockDelegate && window.NoteBlockDelegate._activeRef;
    if (!ref) return;
    const relativePath = await ref.invokeMethodAsync('SaveClipboardImageAsync');
    if (!relativePath) return;
    // Insert into TipTap editor if available, otherwise into fallback textarea
    const editorEl = document.querySelector('.note-block.editing .tiptap-editor');
    if (editorEl && window.TipTapBridge) {
        window.TipTapBridge.insertMarkdown(editorEl.id, `![](${relativePath})`);
    } else {
        const ta = document.querySelector('.note-block.editing textarea');
        if (ta) {
            const pos = ta.selectionStart ?? ta.value.length;
            ta.value = ta.value.slice(0, pos) + `![](${relativePath})` + ta.value.slice(pos);
            ta.dispatchEvent(new Event('input'));
        }
    }
}, true);
