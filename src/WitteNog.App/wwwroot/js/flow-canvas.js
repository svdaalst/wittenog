// flow-canvas.js — FlowBlockDelegate (preview) + FlowCanvasDelegate (editor)

// ─── FlowBlockDelegate ──────────────────────────────────────────────────────
// Handles clicks in the read-only SVG preview rendered by FlowToSvgConverter.
// Follows the same single-listener pattern as NoteBlockDelegate.

window.FlowBlockDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', e => {
            // WikiLink tspan clicks (inside SVG preview)
            const wiki = e.target.closest('[data-wikilink]');
            if (wiki) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('NavigateToWikiLink',
                    wiki.dataset.wikilink, e.shiftKey);
                return;
            }
            // Action buttons (edit, delete, confirm)
            const action = e.target.closest('[data-action]');
            if (action) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleFlowAction', action.dataset.action);
            }
        });
    },

    renderPreview(svgEl, diagramJson) {
        if (!svgEl) return;
        const state = JSON.parse(diagramJson);
        const nodes = state.nodes || [];
        const edges = state.edges || [];
        const ns = 'http://www.w3.org/2000/svg';

        svgEl.innerHTML = '';

        // Compute viewBox to fit all nodes
        if (nodes.length === 0) {
            svgEl.setAttribute('viewBox', '0 0 400 120');
        } else {
            const pad = 16;
            const minX = Math.min(...nodes.map(n => n.x)) - pad;
            const minY = Math.min(...nodes.map(n => n.y)) - pad;
            const maxX = Math.max(...nodes.map(n => n.x + n.width)) + pad;
            const maxY = Math.max(...nodes.map(n => n.y + n.height)) + pad;
            svgEl.setAttribute('viewBox', `${minX} ${minY} ${maxX - minX} ${maxY - minY}`);
        }

        // Arrow marker (unique id per SVG element to avoid collisions)
        const markerId = 'prev-arrow-' + Math.random().toString(36).slice(2);
        const defs = document.createElementNS(ns, 'defs');
        const marker = document.createElementNS(ns, 'marker');
        marker.setAttribute('id', markerId);
        marker.setAttribute('markerWidth', '10');
        marker.setAttribute('markerHeight', '7');
        marker.setAttribute('refX', '9');
        marker.setAttribute('refY', '3.5');
        marker.setAttribute('orient', 'auto');
        const poly = document.createElementNS(ns, 'polygon');
        poly.setAttribute('points', '0 0, 10 3.5, 0 7');
        poly.setAttribute('class', 'flow-arrow-head');
        marker.appendChild(poly);
        defs.appendChild(marker);
        svgEl.appendChild(defs);

        // Edges
        for (const edge of edges) {
            const from = nodes.find(n => n.id === edge.fromNodeId);
            const to = nodes.find(n => n.id === edge.toNodeId);
            if (!from || !to) continue;
            const x1 = from.x + from.width / 2;
            const y1 = from.y + from.height / 2;
            const x2 = to.x + to.width / 2;
            const y2 = to.y + to.height / 2;
            const mx = (x1 + x2) / 2;
            const my = (y1 + y2) / 2 - 20;
            const path = document.createElementNS(ns, 'path');
            path.setAttribute('d', `M ${x1} ${y1} Q ${mx} ${my} ${x2} ${y2}`);
            path.setAttribute('class', 'flow-edge');
            path.setAttribute('marker-end', `url(#${markerId})`);
            svgEl.appendChild(path);

            if (edge.label) {
                const lbl = document.createElementNS(ns, 'text');
                lbl.setAttribute('x', mx);
                lbl.setAttribute('y', my - 4);
                lbl.setAttribute('text-anchor', 'middle');
                lbl.setAttribute('class', 'flow-edge-label');
                lbl.textContent = edge.label;
                svgEl.appendChild(lbl);
            }
        }

        // Nodes
        for (const node of nodes) {
            const g = document.createElementNS(ns, 'g');
            let shape;
            if (node.shape === 'diamond') {
                const cx = node.x + node.width / 2, cy = node.y + node.height / 2;
                shape = document.createElementNS(ns, 'polygon');
                shape.setAttribute('points',
                    `${cx},${node.y} ${node.x + node.width},${cy} ${cx},${node.y + node.height} ${node.x},${cy}`);
            } else if (node.shape === 'ellipse') {
                shape = document.createElementNS(ns, 'ellipse');
                shape.setAttribute('cx', node.x + node.width / 2);
                shape.setAttribute('cy', node.y + node.height / 2);
                shape.setAttribute('rx', node.width / 2);
                shape.setAttribute('ry', node.height / 2);
            } else {
                shape = document.createElementNS(ns, 'rect');
                shape.setAttribute('x', node.x);
                shape.setAttribute('y', node.y);
                shape.setAttribute('width', node.width);
                shape.setAttribute('height', node.height);
                shape.setAttribute('rx', '4');
            }
            shape.setAttribute('class', 'flow-node-shape');
            g.appendChild(shape);

            const txt = document.createElementNS(ns, 'text');
            txt.setAttribute('x', node.x + node.width / 2);
            txt.setAttribute('y', node.y + node.height / 2 + 5);
            txt.setAttribute('text-anchor', 'middle');
            txt.setAttribute('class', 'flow-node-text');
            txt.textContent = node.text;
            g.appendChild(txt);

            svgEl.appendChild(g);
        }
    }
};

// ─── FlowCanvasDelegate ─────────────────────────────────────────────────────
// Interactive SVG-based canvas editor. No external library required.
// State is maintained in JS until the user explicitly clicks Save.

window.FlowCanvasDelegate = {
    _instances: {},

    attach(svgEl, overlayEl, dotNetRef, diagramJson) {
        const id = svgEl.id;
        const state = JSON.parse(diagramJson);
        // Ensure arrays
        state.nodes = state.nodes || [];
        state.edges = state.edges || [];

        const inst = {
            svgEl,
            overlayEl,
            dotNetRef,
            state,
            selected: null,       // { type: 'node'|'edge', id }
            connectMode: false,
            connectFrom: null,    // node id when drawing an edge
            drag: null,           // { nodeId, startX, startY, origX, origY }
            edgePreview: null,    // SVG line element for in-progress edge
        };

        this._instances[id] = inst;
        this._render(id);
        this._attachEvents(id);
    },

    detach(id) {
        const inst = this._instances[id];
        if (!inst) return;
        inst.svgEl.innerHTML = '';
        delete this._instances[id];
    },

    // ── Rendering ────────────────────────────────────────────────────────────

    _render(id) {
        const inst = this._instances[id];
        if (!inst) return;
        const svg = inst.svgEl;
        svg.innerHTML = '';

        const ns = 'http://www.w3.org/2000/svg';

        // Arrow marker def
        const defs = document.createElementNS(ns, 'defs');
        const marker = document.createElementNS(ns, 'marker');
        marker.setAttribute('id', `arrow-${id}`);
        marker.setAttribute('markerWidth', '10');
        marker.setAttribute('markerHeight', '7');
        marker.setAttribute('refX', '9');
        marker.setAttribute('refY', '3.5');
        marker.setAttribute('orient', 'auto');
        const poly = document.createElementNS(ns, 'polygon');
        poly.setAttribute('points', '0 0, 10 3.5, 0 7');
        poly.setAttribute('class', 'flow-arrow-head');
        marker.appendChild(poly);
        defs.appendChild(marker);
        svg.appendChild(defs);

        // Edges
        for (const edge of inst.state.edges) {
            const fromNode = inst.state.nodes.find(n => n.id === edge.fromNodeId);
            const toNode = inst.state.nodes.find(n => n.id === edge.toNodeId);
            if (!fromNode || !toNode) continue;

            const x1 = fromNode.x + fromNode.width / 2;
            const y1 = fromNode.y + fromNode.height / 2;
            const x2 = toNode.x + toNode.width / 2;
            const y2 = toNode.y + toNode.height / 2;
            const mx = (x1 + x2) / 2;
            const my = (y1 + y2) / 2 - 20;

            const path = document.createElementNS(ns, 'path');
            path.setAttribute('d', `M ${x1} ${y1} Q ${mx} ${my} ${x2} ${y2}`);
            path.setAttribute('class', 'flow-edge' + (inst.selected?.type === 'edge' && inst.selected.id === edge.id ? ' selected' : ''));
            path.setAttribute('marker-end', `url(#arrow-${id})`);
            path.setAttribute('data-edge-id', edge.id);
            svg.appendChild(path);

            if (edge.label) {
                const txt = document.createElementNS(ns, 'text');
                txt.setAttribute('x', mx);
                txt.setAttribute('y', my - 4);
                txt.setAttribute('text-anchor', 'middle');
                txt.setAttribute('class', 'flow-edge-label');
                txt.textContent = edge.label;
                svg.appendChild(txt);
            }
        }

        // Nodes
        for (const node of inst.state.nodes) {
            const g = document.createElementNS(ns, 'g');
            g.setAttribute('class', 'flow-node' + (inst.selected?.type === 'node' && inst.selected.id === node.id ? ' selected' : ''));
            g.setAttribute('data-node-id', node.id);

            // Shape
            let shape;
            if (node.shape === 'diamond') {
                const cx = node.x + node.width / 2, cy = node.y + node.height / 2;
                shape = document.createElementNS(ns, 'polygon');
                shape.setAttribute('points', `${cx},${node.y} ${node.x + node.width},${cy} ${cx},${node.y + node.height} ${node.x},${cy}`);
            } else if (node.shape === 'ellipse') {
                shape = document.createElementNS(ns, 'ellipse');
                shape.setAttribute('cx', node.x + node.width / 2);
                shape.setAttribute('cy', node.y + node.height / 2);
                shape.setAttribute('rx', node.width / 2);
                shape.setAttribute('ry', node.height / 2);
            } else {
                shape = document.createElementNS(ns, 'rect');
                shape.setAttribute('x', node.x);
                shape.setAttribute('y', node.y);
                shape.setAttribute('width', node.width);
                shape.setAttribute('height', node.height);
                shape.setAttribute('rx', '4');
            }
            shape.setAttribute('class', 'flow-node-shape');
            g.appendChild(shape);

            // Text (simple single-line; newlines shown as spaces in editor)
            const txt = document.createElementNS(ns, 'text');
            txt.setAttribute('x', node.x + node.width / 2);
            txt.setAttribute('y', node.y + node.height / 2 + 5);
            txt.setAttribute('text-anchor', 'middle');
            txt.setAttribute('class', 'flow-node-text');
            txt.textContent = node.text;
            g.appendChild(txt);

            // Connect handle (small circle on right edge, visible on hover/select)
            const handle = document.createElementNS(ns, 'circle');
            handle.setAttribute('cx', node.x + node.width);
            handle.setAttribute('cy', node.y + node.height / 2);
            handle.setAttribute('r', '6');
            handle.setAttribute('class', 'flow-connect-handle');
            handle.setAttribute('data-connect-from', node.id);
            g.appendChild(handle);

            svg.appendChild(g);
        }
    },

    // ── Events ───────────────────────────────────────────────────────────────

    _attachEvents(id) {
        const inst = this._instances[id];
        if (!inst) return;
        const svg = inst.svgEl;
        const overlay = inst.overlayEl;
        const self = this;

        // ── Toolbar actions ──
        overlay.addEventListener('click', e => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            const action = btn.dataset.action;

            if (action === 'save-canvas') {
                const json = JSON.stringify({
                    nodes: inst.state.nodes,
                    edges: inst.state.edges
                });
                inst.dotNetRef.invokeMethodAsync('SaveDiagram', json);

            } else if (action === 'close-canvas') {
                inst.dotNetRef.invokeMethodAsync('CloseCanvas');

            } else if (action === 'add-node') {
                self._addNode(id, 80, 80);

            } else if (action === 'toggle-connect') {
                inst.connectMode = !inst.connectMode;
                btn.classList.toggle('active', inst.connectMode);
                inst.connectFrom = null;
                svg.style.cursor = inst.connectMode ? 'crosshair' : 'default';
            }
        });

        // ── SVG interactions ──
        // Double-click on empty canvas → add node
        svg.addEventListener('dblclick', e => {
            if (e.target === svg || e.target.closest('defs')) {
                const pt = self._svgPoint(svg, e);
                self._addNode(id, pt.x - 80, pt.y - 30);
            }
        });

        // pendingEdit: nodeId of a selected node that was clicked without dragging.
        // If mouseup fires without significant movement we open the text editor.
        // This avoids all timing-based double-click detection (unreliable in WebView2).
        // UX: first click = select, second click on selected node = edit, drag = drag.
        let pendingEdit = null;
        const DRAG_THRESHOLD = 5; // px — movement beyond this commits to a drag

        svg.addEventListener('mousedown', e => {
            const handle = e.target.closest('[data-connect-from]');
            if (handle) {
                e.stopPropagation();
                inst.connectFrom = handle.dataset.connectFrom;
                const pt = self._svgPoint(svg, e);
                const ns = 'http://www.w3.org/2000/svg';
                inst.edgePreview = document.createElementNS(ns, 'line');
                inst.edgePreview.setAttribute('x1', pt.x);
                inst.edgePreview.setAttribute('y1', pt.y);
                inst.edgePreview.setAttribute('x2', pt.x);
                inst.edgePreview.setAttribute('y2', pt.y);
                inst.edgePreview.setAttribute('class', 'flow-edge-preview');
                svg.appendChild(inst.edgePreview);
                return;
            }

            const nodeG = e.target.closest('[data-node-id]');
            if (nodeG) {
                e.stopPropagation();
                const nodeId = nodeG.dataset.nodeId;
                const node = inst.state.nodes.find(n => n.id === nodeId);
                const pt = self._svgPoint(svg, e);

                if (inst.selected?.type === 'node' && inst.selected.id === nodeId) {
                    // Node already selected — next mouseup (without drag) opens editor
                    pendingEdit = nodeId;
                    if (node) {
                        inst.drag = { nodeId, startMouseX: pt.x, startMouseY: pt.y, origX: node.x, origY: node.y };
                    }
                    // No re-render needed: node is already shown as selected
                } else {
                    // Select the node
                    pendingEdit = null;
                    inst.selected = { type: 'node', id: nodeId };
                    if (node) {
                        inst.drag = { nodeId, startMouseX: pt.x, startMouseY: pt.y, origX: node.x, origY: node.y };
                    }
                    self._render(id);
                }
                return;
            }

            const edgePath = e.target.closest('[data-edge-id]');
            if (edgePath) {
                pendingEdit = null;
                inst.selected = { type: 'edge', id: edgePath.dataset.edgeId };
                self._render(id);
                return;
            }

            // Click on empty canvas — deselect
            pendingEdit = null;
            inst.selected = null;
            self._render(id);
        });

        svg.addEventListener('mousemove', e => {
            const pt = self._svgPoint(svg, e);

            if (inst.edgePreview) {
                inst.edgePreview.setAttribute('x2', pt.x);
                inst.edgePreview.setAttribute('y2', pt.y);
                return;
            }

            if (inst.drag) {
                const dx = pt.x - inst.drag.startMouseX;
                const dy = pt.y - inst.drag.startMouseY;
                if (Math.abs(dx) > DRAG_THRESHOLD || Math.abs(dy) > DRAG_THRESHOLD) {
                    pendingEdit = null; // moved too far — commit to drag, not edit
                }
                const node = inst.state.nodes.find(n => n.id === inst.drag.nodeId);
                if (node) {
                    node.x = inst.drag.origX + dx;
                    node.y = inst.drag.origY + dy;
                    self._render(id);
                }
            }
        });

        svg.addEventListener('mouseup', e => {
            if (inst.edgePreview) {
                inst.edgePreview.remove();
                inst.edgePreview = null;

                if (inst.connectFrom) {
                    const targetG = e.target.closest('[data-node-id]');
                    if (targetG && targetG.dataset.nodeId !== inst.connectFrom) {
                        self._addEdge(id, inst.connectFrom, targetG.dataset.nodeId);
                    }
                    inst.connectFrom = null;
                }
                return;
            }

            const editNodeId = pendingEdit;
            pendingEdit = null;
            inst.drag = null;

            if (editNodeId) {
                self._editNodeText(id, editNodeId);
            }
        });

        // Keyboard shortcuts
        document.addEventListener('keydown', function onKey(e) {
            if (!document.contains(svg)) {
                document.removeEventListener('keydown', onKey);
                return;
            }
            // Skip if an input/textarea inside the overlay has focus
            if (overlay.querySelector('input:focus, textarea:focus')) return;

            if (e.key === 'F2' && inst.selected?.type === 'node') {
                e.preventDefault();
                self._editNodeText(id, inst.selected.id);

            } else if (e.key === 'Delete' || e.key === 'Backspace') {
                if (inst.selected?.type === 'node') {
                    inst.state.nodes = inst.state.nodes.filter(n => n.id !== inst.selected.id);
                    inst.state.edges = inst.state.edges.filter(
                        ed => ed.fromNodeId !== inst.selected.id && ed.toNodeId !== inst.selected.id);
                    inst.selected = null;
                    self._render(id);
                } else if (inst.selected?.type === 'edge') {
                    inst.state.edges = inst.state.edges.filter(ed => ed.id !== inst.selected.id);
                    inst.selected = null;
                    self._render(id);
                }
            }
        });
    },

    // ── Helpers ───────────────────────────────────────────────────────────────

    _addNode(id, x, y) {
        const inst = this._instances[id];
        const nodeId = 'n' + Date.now();
        inst.state.nodes.push({ id: nodeId, x, y, width: 160, height: 60, text: 'Nieuwe node', shape: 'rect' });
        inst.selected = { type: 'node', id: nodeId };
        this._render(id);
        this._editNodeText(id, nodeId);
    },

    _addEdge(id, fromId, toId) {
        const inst = this._instances[id];
        // Prevent duplicate edges
        const exists = inst.state.edges.some(e => e.fromNodeId === fromId && e.toNodeId === toId);
        if (exists) return;
        inst.state.edges.push({ id: 'e' + Date.now(), fromNodeId: fromId, toNodeId: toId, label: null });
        this._render(id);
    },

    _editNodeText(id, nodeId) {
        const inst = this._instances[id];
        const node = inst.state.nodes.find(n => n.id === nodeId);
        if (!node) return;

        const svg = inst.svgEl;
        const overlay = inst.overlayEl;

        // Remove any existing editor
        const existing = overlay.querySelector('.flow-text-editor-input');
        if (existing) existing.remove();

        // Convert node SVG coords → screen coords → coords relative to overlay
        const svgRect = svg.getBoundingClientRect();
        const overlayRect = overlay.getBoundingClientRect();
        const vb = svg.viewBox?.baseVal;

        let left, top, width, height;
        if (vb && vb.width > 0) {
            const scaleX = svgRect.width / vb.width;
            const scaleY = svgRect.height / vb.height;
            left = (svgRect.left - overlayRect.left) + (node.x - vb.x) * scaleX;
            top  = (svgRect.top  - overlayRect.top)  + (node.y - vb.y) * scaleY;
            width  = node.width  * scaleX;
            height = node.height * scaleY;
        } else {
            left   = (svgRect.left - overlayRect.left) + node.x;
            top    = (svgRect.top  - overlayRect.top)  + node.y;
            width  = node.width;
            height = node.height;
        }

        const input = document.createElement('input');
        input.type = 'text';
        input.value = node.text;
        input.className = 'flow-text-editor-input';
        input.style.cssText = [
            `position:absolute`,
            `left:${left}px`, `top:${top}px`,
            `width:${width}px`, `height:${height}px`,
            `box-sizing:border-box`,
            `background:var(--surface-2)`, `color:var(--text)`,
            `border:2px solid var(--accent)`, `border-radius:4px`,
            `padding:0 8px`, `font-size:13px`, `text-align:center`,
            `z-index:10`
        ].join(';');

        const commit = () => {
            node.text = input.value;
            input.remove();
            this._render(id);
        };

        input.addEventListener('blur', commit);
        input.addEventListener('keydown', e => {
            if (e.key === 'Enter')  { commit(); }
            else if (e.key === 'Escape') { input.remove(); this._render(id); }
            e.stopPropagation(); // Prevent Delete key listener from firing
        });

        overlay.appendChild(input);
        input.focus();
        input.select();
    },

    _svgPoint(svg, e) {
        const rect = svg.getBoundingClientRect();
        // Account for viewBox scaling if set
        const vb = svg.viewBox?.baseVal;
        if (vb && vb.width > 0) {
            const scaleX = vb.width / rect.width;
            const scaleY = vb.height / rect.height;
            return {
                x: (e.clientX - rect.left) * scaleX + vb.x,
                y: (e.clientY - rect.top) * scaleY + vb.y
            };
        }
        return { x: e.clientX - rect.left, y: e.clientY - rect.top };
    }
};
