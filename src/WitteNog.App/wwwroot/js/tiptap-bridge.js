// Lees de waarde van een input direct uit de DOM (omzeilt @bind die niet werkt in BlazorWebView)
window.getInputValue = function(id) {
    const el = document.getElementById(id);
    return el ? el.value : '';
};

// Onboarding button delegation — zelfde patroon als WikiLinks
window.OnboardingDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', (e) => {
            const btn = e.target.closest('[data-action]');
            if (btn) dotNetRef.invokeMethodAsync('HandleAction', btn.dataset.action);
        });
    }
};

// Tab bar delegation — passes both action and tabid
window.TabDelegate = {
    attach(element, dotNetRef) {
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
            // Handle note actions
            const actionTarget = e.target.closest('[data-action]');
            if (actionTarget) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleNoteAction', actionTarget.dataset.action);
            }
        });
        element.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'Enter') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('HandleNoteAction', 'save');
            }
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
            ta.style.cssText = 'width:100%;min-height:120px;background:transparent;border:none;outline:none;resize:vertical;font-family:inherit;font-size:inherit;color:inherit;';
            ta.dataset.fallback = 'true';
            el.appendChild(ta);
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

    getContent(elementId) {
        const editor = this.editors[elementId];
        if (!editor) return '';
        if (editor.isFallback) return editor.el.value;
        return editor.getText ? editor.getText() : editor.getHTML();
    },

    destroy(elementId) {
        const editor = this.editors[elementId];
        if (!editor) return;
        if (!editor.isFallback) editor.destroy();
        delete this.editors[elementId];
    }
};
