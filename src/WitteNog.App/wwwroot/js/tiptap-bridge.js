// Lees de waarde van een input direct uit de DOM (omzeilt @bind die niet werkt in BlazorWebView)
window.getInputValue = function(id) {
    const el = document.getElementById(id);
    return el ? el.value : '';
};

// Lees de checked-status van een checkbox direct uit de DOM
window.getCheckboxChecked = function(id) {
    const el = document.getElementById(id);
    return el ? el.checked : false;
};

// Onboarding button delegation — zelfde patroon als WikiLinks
window.OnboardingDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            // If the click bubbled up from inside a .modal box to a parent with
            // data-action (the backdrop overlay has data-action="cancel"), ignore it.
            // Only a direct click on the backdrop itself should close the modal.
            if (btn !== e.target && e.target.closest('.modal')) return;
            dotNetRef.invokeMethodAsync('HandleAction', btn.dataset.action);
        });
    }
};

// Task dashboard delegation — passes action and taskid or filepath
window.TaskTableDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            const target = e.target.closest('[data-action]');
            if (target) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleAction',
                    target.dataset.action,
                    target.dataset.wikilink || target.dataset.filepath || target.dataset.taskid || '');
            }
        });
        element.addEventListener('change', (e) => {
            const target = e.target.closest('[data-task-field]');
            if (target) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleFieldChange',
                    target.dataset.taskid,
                    target.dataset.taskField,
                    target.value);
            }
        });
    }
};

window.TaskDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            const target = e.target.closest('[data-action]');
            if (target) {
                dotNetRef.invokeMethodAsync('HandleAction',
                    target.dataset.action,
                    target.dataset.wikilink || target.dataset.filepath || target.dataset.taskid || '');
            }
        });
        element.addEventListener('change', (e) => {
            const target = e.target.closest('[data-task-field]');
            if (target) {
                dotNetRef.invokeMethodAsync('HandleFieldChange',
                    target.dataset.taskid,
                    target.dataset.taskField,
                    target.value);
            }
        });
    }
};

// Scroll to a note anchor by note ID, retrying until the DOM is ready
window.scrollToNote = function(noteId, attempt) {
    attempt = attempt || 0;
    const el = document.getElementById('note-' + noteId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        el.classList.add('note-highlight');
        setTimeout(() => el.classList.remove('note-highlight'), 1500);
        return;
    }
    if (attempt < 20) {
        setTimeout(() => window.scrollToNote(noteId, attempt + 1), 100);
    }
};

// Tab bar delegation — passes both action and tabid
window.TabDelegate = {
    _ref: null,
    setRef(dotNetRef) { window.TabDelegate._ref = dotNetRef; },
    attach(element, dotNetRef) {
        window.TabDelegate._ref = dotNetRef;
        element.addEventListener('click', (e) => {
            const target = e.target.closest('[data-action]');
            if (target) {
                dotNetRef.invokeMethodAsync('HandleAction',
                    target.dataset.action,
                    target.dataset.tabid || '');
            }
        });
    }
};

// NoteBlock delegation — handles WikiLinks, audio links, and note actions
window.NoteBlockDelegate = {
    _activeRef: null,

    setActiveRef(dotNetRef) { window.NoteBlockDelegate._activeRef = dotNetRef; },
    clearActiveRef()        { window.NoteBlockDelegate._activeRef = null; },

    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            // Handle wiki links first
            const wikiTarget = e.target.closest('[data-wikilink]');
            if (wikiTarget) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('NavigateToWikiLink', wikiTarget.dataset.wikilink, e.shiftKey);
                return;
            }
            // Handle audio file links — open via OS instead of navigating
            const audioTarget = e.target.closest('[data-audiolink]');
            if (audioTarget) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('OpenAudioFile', audioTarget.dataset.audiolink);
                return;
            }
            // Handle note actions — but NOT 'edit', which requires a double-click
            const actionTarget = e.target.closest('[data-action]');
            if (actionTarget && actionTarget.dataset.action !== 'edit') {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleNoteAction',
                    actionTarget.dataset.action,
                    actionTarget.dataset.taskid || '');
                return;
            }
            // Title bar click (not on interactive elements) toggles note collapse
            if (e.target.closest('.note-title-bar')) {
                e.stopPropagation();
                element.classList.toggle('note-collapsed');
            }
        });
        // Double-click on note content enters edit mode
        element.addEventListener('dblclick', (e) => {
            const actionTarget = e.target.closest('[data-action="edit"]');
            if (actionTarget) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleNoteAction', 'edit', '');
            }
        });
        // Image paste: save to vault and insert markdown image syntax
        element.addEventListener('paste', async (e) => {
            const items = Array.from(e.clipboardData?.items || []);
            const imageItem = items.find(i => i.type.startsWith('image/'));
            if (!imageItem) return;
            const editorEl = element.querySelector('.tiptap-editor');
            if (!editorEl) return;
            e.preventDefault();
            const blob = imageItem.getAsFile();
            const ext = imageItem.type.split('/')[1] || 'png';
            const base64 = await new Promise(resolve => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result.split(',')[1]);
                reader.readAsDataURL(blob);
            });
            const relativePath = await dotNetRef.invokeMethodAsync('SavePastedImageAsync', base64, ext);
            TipTapBridge.insertMarkdown(editorEl.id, `![](${relativePath})`);
        });
    }
};

// Collapsible headings — wraps content after each heading in a toggleable section
window.HeadingCollapse = {
    initBlock(container) {
        const content = container.querySelector('.note-content');
        if (!content) return; // note is in edit mode

        if (content.dataset.hcInit) this._unwrap(content);

        this._wrapSections(content);
        content.dataset.hcInit = '1';
    },

    _wrapSections(container) {
        const allHeadings = ['H1','H2','H3','H4','H5','H6'];
        const children = Array.from(container.childNodes);

        // Find the shallowest heading level present as direct children
        let topLevel = null;
        for (const node of children) {
            if (node.nodeType === Node.ELEMENT_NODE) {
                const idx = allHeadings.indexOf(node.tagName);
                if (idx !== -1 && (topLevel === null || idx < topLevel)) topLevel = idx;
            }
        }
        if (topLevel === null) return; // no headings in this container

        const headingTag = allHeadings[topLevel];
        let currentHeading = null;
        let buffer = [];

        const flush = () => {
            if (!currentHeading || buffer.length === 0) return;
            const wrapper = document.createElement('div');
            wrapper.className = 'heading-section';
            currentHeading.insertAdjacentElement('afterend', wrapper);
            for (const node of buffer) wrapper.appendChild(node);
            this._wrapSections(wrapper); // recurse for sub-headings
            buffer = [];
        };

        for (const node of children) {
            if (node.nodeType === Node.ELEMENT_NODE && node.tagName === headingTag) {
                flush();
                currentHeading = node;
                this._attachToggle(node);
            } else if (currentHeading) {
                buffer.push(node);
            }
        }
        flush();
    },

    _attachToggle(heading) {
        const toggle = document.createElement('span');
        toggle.className = 'heading-toggle';
        toggle.textContent = '▾';
        toggle.addEventListener('click', (e) => {
            e.stopPropagation(); // prevents NoteBlockDelegate from firing edit mode
            const section = heading.nextElementSibling;
            if (!section || !section.classList.contains('heading-section')) return;
            const isCollapsed = section.classList.toggle('collapsed');
            toggle.textContent = isCollapsed ? '▸' : '▾';
        });
        heading.prepend(toggle);
    },

    _unwrap(content) {
        // Process innermost sections first (reverse document order) to restore flat structure
        const sections = Array.from(content.querySelectorAll('.heading-section')).reverse();
        for (const section of sections) {
            while (section.firstChild) section.parentNode.insertBefore(section.firstChild, section);
            section.remove();
        }
        for (const toggle of content.querySelectorAll('.heading-toggle')) toggle.remove();
        delete content.dataset.hcInit;
    },

    setAll(canvasElement, collapse) {
        for (const s of canvasElement.querySelectorAll('.heading-section'))
            s.classList.toggle('collapsed', collapse);
        for (const t of canvasElement.querySelectorAll('.heading-toggle'))
            t.textContent = collapse ? '▸' : '▾';
        for (const nb of canvasElement.querySelectorAll('.note-block:not(.editing)'))
            nb.classList.toggle('note-collapsed', collapse);
    }
};

// WikiLink event delegation — attach a real DOM listener so event.target is available
window.WikiLinkDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            const target = e.target.closest('[data-wikilink]');
            if (target) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('NavigateToWikiLink', target.dataset.wikilink, e.shiftKey);
            }
        });
    }
};

window.TipTapBridge = {
    editors: {},

    init(elementId, initialContent) {
        const el = document.getElementById(elementId);
        if (!el) return;

        // Use raw markdown as initial text content
        el.innerHTML = '';

        if (typeof window.tiptap === 'undefined' || typeof window.tiptapStarterKit === 'undefined') {
            // Fallback: plain textarea if TipTap not loaded
            const ta = document.createElement('textarea');
            ta.value = initialContent;
            ta.style.cssText = 'width:100%;background:transparent;border:none;outline:none;resize:none;overflow:hidden;font-family:inherit;font-size:inherit;color:inherit;display:block;';
            ta.dataset.fallback = 'true';
            el.appendChild(ta);
            // Auto-resize: toon volledige inhoud zonder interne scroll
            const autoResize = () => { ta.style.height = 'auto'; ta.style.height = ta.scrollHeight + 'px'; };
            autoResize();
            ta.addEventListener('input', autoResize);
            this.editors[elementId] = { isFallback: true, el: ta };
            return;
        }

        this.editors[elementId] = new window.tiptap.Editor({
            element: el,
            extensions: [window.tiptapStarterKit.StarterKit],
            content: initialContent,
            editorProps: {
                attributes: {
                    class: 'tiptap-content',
                },
            },
        });
    },

    insertMarkdown(elementId, text) {
        const editor = this.editors[elementId];
        if (!editor || editor.isFallback) return;
        editor.commands.insertContent({ type: 'text', text });
    },

    getContent(elementId) {
        const editor = this.editors[elementId];
        if (!editor) return '';
        if (editor.isFallback) return editor.el.value;
        return this._toMarkdown(editor.getJSON());
    },

    _inlineText(node) {
        if (node.type === 'text') {
            let t = node.text || '';
            for (const m of (node.marks || [])) {
                if (m.type === 'bold')   t = `**${t}**`;
                if (m.type === 'italic') t = `*${t}*`;
                if (m.type === 'code')   t = `\`${t}\``;
            }
            return t;
        }
        return (node.content || []).map(n => this._inlineText(n)).join('');
    },

    _toMarkdown(doc) {
        const parts = [];
        for (const node of (doc.content || [])) {
            switch (node.type) {
                case 'heading': {
                    const hashes = '#'.repeat(node.attrs?.level || 1);
                    const text = (node.content || []).map(n => this._inlineText(n)).join('');
                    parts.push(`${hashes} ${text}`);
                    break;
                }
                case 'paragraph': {
                    const text = (node.content || []).map(n => this._inlineText(n)).join('');
                    parts.push(text);
                    break;
                }
                case 'bulletList': {
                    for (const item of (node.content || [])) {
                        const text = (item.content || []).flatMap(p => (p.content || []).map(n => this._inlineText(n))).join('');
                        parts.push(`- ${text}`);
                    }
                    break;
                }
                case 'orderedList': {
                    (node.content || []).forEach((item, i) => {
                        const text = (item.content || []).flatMap(p => (p.content || []).map(n => this._inlineText(n))).join('');
                        parts.push(`${i + 1}. ${text}`);
                    });
                    break;
                }
                case 'blockquote': {
                    const text = (node.content || []).flatMap(p => (p.content || []).map(n => this._inlineText(n))).join('');
                    parts.push(`> ${text}`);
                    break;
                }
                case 'codeBlock': {
                    const text = (node.content || []).map(n => n.text || '').join('');
                    const lang = node.attrs?.language || '';
                    parts.push(`\`\`\`${lang}\n${text}\n\`\`\``);
                    break;
                }
                case 'horizontalRule':
                    parts.push('---');
                    break;
            }
        }
        return parts.join('\n\n');
    },

    destroy(elementId) {
        const editor = this.editors[elementId];
        if (!editor) return;
        if (!editor.isFallback) editor.destroy();
        delete this.editors[elementId];
    }
};
