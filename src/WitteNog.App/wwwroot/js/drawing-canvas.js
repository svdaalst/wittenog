// drawing-canvas.js
// Two delegates mirroring the flow-canvas.js pattern:
//   DrawingBlockDelegate  – click delegation on read-only preview blocks
//   DrawingCanvasDelegate – interactive freehand drawing canvas

// ─────────────────────────────────────────────────────────────────────────────
// DrawingBlockDelegate
// Attaches a single click listener to the .drawing-block container and routes
// data-action clicks back to the Blazor component via DotNetObjectReference.
// ─────────────────────────────────────────────────────────────────────────────
window.DrawingBlockDelegate = {
    attach(element, dotNetRef) {
        element.addEventListener('click', e => {
            const target = e.target.closest('[data-action]');
            if (!target) return;
            e.stopPropagation();
            dotNetRef.invokeMethodAsync('HandleDrawingAction', target.dataset.action);
        });

        // Double-click on the preview image (or the empty-hint) opens the canvas editor
        element.addEventListener('dblclick', e => {
            const preview = e.target.closest('.drawing-preview');
            if (preview) {
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('HandleDrawingAction', 'edit-drawing');
            }
        });
    }
};

// ─────────────────────────────────────────────────────────────────────────────
// DrawingCanvasDelegate
// Multi-instance interactive drawing canvas.
// State is keyed by canvas element id (_instances map).
// ─────────────────────────────────────────────────────────────────────────────
window.DrawingCanvasDelegate = (function () {
    const _instances = {};

    // ── Attach ──────────────────────────────────────────────────────────────
    function attach(canvasEl, overlayEl, dotNetRef, payloadJson) {
        const payload = typeof payloadJson === 'string'
            ? JSON.parse(payloadJson)
            : payloadJson;

        const width  = (payload.width  > 0 ? payload.width  : 1920);
        const height = (payload.height > 0 ? payload.height : 1080);

        canvasEl.width  = width;
        canvasEl.height = height;

        const ctx = canvasEl.getContext('2d');

        const inst = {
            canvas:          canvasEl,
            ctx:             ctx,
            overlayEl:       overlayEl,
            dotNetRef:       dotNetRef,
            width:           width,
            height:          height,
            tool:            'pen',
            color:           '#ffffff',
            thickness:       4,
            background:      payload.background || 'plain',
            isDrawing:       false,
            activePointerId: null,
            prevPoint:       null,
            currPoint:       null,
            // Pointer-event bound listeners (stored for clean removal)
            _onPointerDown:  null,
            _onPointerMove:  null,
            _onPointerUp:    null,
            _onPointerCancel:null,
        };

        // Load existing image onto canvas so the user can keep editing
        if (payload.imageBase64 && payload.imageBase64.length > 100) {
            const img = new Image();
            img.onload = () => ctx.drawImage(img, 0, 0);
            img.src = 'data:image/png;base64,' + payload.imageBase64;
        }

        _instances[canvasEl.id] = inst;
        _attachPointerEvents(canvasEl.id);
        _attachToolbarEvents(canvasEl.id);
    }

    // ── Detach ──────────────────────────────────────────────────────────────
    function detach(canvasId) {
        const inst = _instances[canvasId];
        if (!inst) return;
        const c = inst.canvas;
        if (inst._onPointerDown)   c.removeEventListener('pointerdown',   inst._onPointerDown);
        if (inst._onPointerMove)   c.removeEventListener('pointermove',   inst._onPointerMove);
        if (inst._onPointerUp)     c.removeEventListener('pointerup',     inst._onPointerUp);
        if (inst._onPointerCancel) c.removeEventListener('pointercancel', inst._onPointerCancel);
        delete _instances[canvasId];
    }

    // ── Pointer events ───────────────────────────────────────────────────────
    function _attachPointerEvents(canvasId) {
        const inst = _instances[canvasId];
        if (!inst) return;
        const c = inst.canvas;

        inst._onPointerDown = e => _onPointerDown(canvasId, e);
        inst._onPointerMove = e => _onPointerMove(canvasId, e);
        inst._onPointerUp   = e => _onPointerUp(canvasId, e);
        inst._onPointerCancel = e => _onPointerUp(canvasId, e);  // treat cancel like up

        c.addEventListener('pointerdown',   inst._onPointerDown);
        c.addEventListener('pointermove',   inst._onPointerMove);
        c.addEventListener('pointerup',     inst._onPointerUp);
        c.addEventListener('pointercancel', inst._onPointerCancel);
    }

    function _getCanvasPoint(inst, e) {
        const rect = inst.canvas.getBoundingClientRect();
        const scaleX = inst.width  / rect.width;
        const scaleY = inst.height / rect.height;
        return {
            x: (e.clientX - rect.left) * scaleX,
            y: (e.clientY - rect.top)  * scaleY,
            // PointerEvent.pressure is 0–1; real value for stylus, 0.5 for mouse
            pressure: (e.pressure > 0) ? e.pressure : 0.5,
        };
    }

    function _onPointerDown(canvasId, e) {
        const inst = _instances[canvasId];
        if (!inst) return;
        // Only respond to primary button / pen contact
        if (e.button !== 0 && e.pointerType === 'mouse') return;

        // Capture so we receive move events even when the pointer leaves the canvas
        inst.canvas.setPointerCapture(e.pointerId);

        inst.isDrawing       = true;
        inst.activePointerId = e.pointerId;
        inst.prevPoint       = null;
        inst.currPoint       = _getCanvasPoint(inst, e);

        // Start a new path segment at the contact point
        const ctx = inst.ctx;
        _configureBrush(inst, inst.currPoint.pressure);
        ctx.beginPath();
        ctx.moveTo(inst.currPoint.x, inst.currPoint.y);
    }

    function _onPointerMove(canvasId, e) {
        const inst = _instances[canvasId];
        if (!inst || !inst.isDrawing) return;
        if (e.pointerId !== inst.activePointerId) return;

        const newPoint = _getCanvasPoint(inst, e);

        if (inst.prevPoint && inst.currPoint) {
            // Three-point quadratic smoothing: draw between midpoints
            _drawSegment(inst, inst.prevPoint, inst.currPoint, newPoint);
        } else if (inst.currPoint) {
            // Only two points so far — draw a straight segment
            const ctx = inst.ctx;
            _configureBrush(inst, newPoint.pressure);
            ctx.beginPath();
            ctx.moveTo(inst.currPoint.x, inst.currPoint.y);
            ctx.lineTo(newPoint.x, newPoint.y);
            ctx.stroke();
        }

        inst.prevPoint = inst.currPoint;
        inst.currPoint = newPoint;
    }

    function _onPointerUp(canvasId, e) {
        const inst = _instances[canvasId];
        if (!inst || !inst.isDrawing) return;
        if (e.pointerId !== inst.activePointerId) return;

        // Draw final dot if the user just tapped (no move)
        if (!inst.prevPoint && inst.currPoint) {
            const ctx = inst.ctx;
            _configureBrush(inst, inst.currPoint.pressure);
            ctx.beginPath();
            ctx.arc(inst.currPoint.x, inst.currPoint.y,
                _lineWidth(inst, inst.currPoint.pressure) / 2, 0, Math.PI * 2);
            ctx.fill();
        }

        inst.isDrawing       = false;
        inst.activePointerId = null;
        inst.prevPoint       = null;
        inst.currPoint       = null;
    }

    // ── Stroke rendering ─────────────────────────────────────────────────────
    function _drawSegment(inst, p0, p1, p2) {
        const ctx = inst.ctx;
        _configureBrush(inst, p1.pressure);
        ctx.beginPath();
        ctx.moveTo((p0.x + p1.x) / 2, (p0.y + p1.y) / 2);
        ctx.quadraticCurveTo(p1.x, p1.y, (p1.x + p2.x) / 2, (p1.y + p2.y) / 2);
        ctx.stroke();
    }

    function _lineWidth(inst, pressure) {
        const t = inst.thickness;
        switch (inst.tool) {
            case 'pen':         return t * (0.5 + pressure * 1.5);
            case 'pencil':      return t * (0.4 + pressure * 0.8);
            case 'highlighter': return t * 4;      // pressure-independent wide stroke
            case 'eraser':      return t * 3;
            default:            return t;
        }
    }

    function _configureBrush(inst, pressure) {
        const ctx = inst.ctx;
        ctx.lineCap  = 'round';
        ctx.lineJoin = 'round';
        ctx.lineWidth = _lineWidth(inst, pressure);

        switch (inst.tool) {
            case 'pen':
                ctx.globalCompositeOperation = 'source-over';
                ctx.globalAlpha = 1.0;
                ctx.strokeStyle = inst.color;
                ctx.fillStyle   = inst.color;
                break;
            case 'pencil':
                ctx.globalCompositeOperation = 'source-over';
                ctx.globalAlpha = 0.6;
                ctx.strokeStyle = inst.color;
                ctx.fillStyle   = inst.color;
                break;
            case 'highlighter':
                // 'multiply' blends the highlight color with what's below.
                // Falls back to 'source-over' if the composite is unsupported.
                ctx.globalCompositeOperation = 'multiply';
                ctx.globalAlpha = 0.3;
                ctx.strokeStyle = inst.color;
                ctx.fillStyle   = inst.color;
                break;
            case 'eraser':
                ctx.globalCompositeOperation = 'destination-out';
                ctx.globalAlpha = 1.0;
                ctx.strokeStyle = 'rgba(0,0,0,1)';
                ctx.fillStyle   = 'rgba(0,0,0,1)';
                break;
        }
    }

    // ── Toolbar events ───────────────────────────────────────────────────────
    function _attachToolbarEvents(canvasId) {
        const inst = _instances[canvasId];
        if (!inst) return;

        inst.overlayEl.addEventListener('click', e => {
            const btn = e.target.closest('[data-action]');
            if (!btn) return;
            const action = btn.dataset.action;

            if (action === 'tool-pen')         { _setTool(inst, 'pen',         inst.overlayEl); return; }
            if (action === 'tool-pencil')      { _setTool(inst, 'pencil',      inst.overlayEl); return; }
            if (action === 'tool-highlighter') { _setTool(inst, 'highlighter', inst.overlayEl); return; }
            if (action === 'tool-eraser')      { _setTool(inst, 'eraser',      inst.overlayEl); return; }

            if (action === 'color') {
                inst.color = btn.dataset.color || '#ffffff';
                _updateActiveColor(inst, inst.overlayEl);
                return;
            }

            if (action === 'thickness') {
                inst.thickness = parseInt(btn.dataset.thickness, 10) || 4;
                _updateActiveThickness(inst, inst.overlayEl);
                return;
            }

            if (action === 'cycle-background') {
                _cycleBackground(inst);
                return;
            }

            if (action === 'save-canvas') {
                _saveCanvas(canvasId);
                return;
            }

            if (action === 'close-canvas') {
                inst.dotNetRef.invokeMethodAsync('CloseCanvas');
                return;
            }
        });

        // Mark initial active state
        _setTool(inst, inst.tool, inst.overlayEl);
        _updateActiveColor(inst, inst.overlayEl);
        _updateActiveThickness(inst, inst.overlayEl);
    }

    function _setTool(inst, tool, overlayEl) {
        inst.tool = tool;
        overlayEl.querySelectorAll('.tool-btn').forEach(b => {
            b.classList.toggle('active', b.dataset.tool === tool);
        });
        // Update canvas cursor
        inst.canvas.style.cursor = tool === 'eraser' ? 'cell' : 'crosshair';
    }

    function _updateActiveColor(inst, overlayEl) {
        overlayEl.querySelectorAll('.color-swatch').forEach(b => {
            b.classList.toggle('active', b.dataset.color === inst.color);
        });
    }

    function _updateActiveThickness(inst, overlayEl) {
        overlayEl.querySelectorAll('.thickness-btn').forEach(b => {
            b.classList.toggle('active',
                parseInt(b.dataset.thickness, 10) === inst.thickness);
        });
    }

    function _cycleBackground(inst) {
        const order = ['plain', 'dot', 'ruled'];
        const next  = order[(order.indexOf(inst.background) + 1) % order.length];
        inst.background = next;

        // Swap CSS class on the wrapper div (canvas stays transparent)
        const wrapper = inst.overlayEl.querySelector('.drawing-canvas-wrapper');
        if (wrapper) {
            wrapper.classList.remove('drawing-bg-plain', 'drawing-bg-dot', 'drawing-bg-ruled');
            wrapper.classList.add('drawing-bg-' + next);
        }

        // Update the label in the toolbar
        const lbl = inst.overlayEl.querySelector('.bg-label');
        if (lbl) lbl.textContent = next.charAt(0).toUpperCase() + next.slice(1);
    }

    // ── Save ────────────────────────────────────────────────────────────────
    function _saveCanvas(canvasId) {
        const inst = _instances[canvasId];
        if (!inst) return;

        // Reset composite before export so toDataURL works correctly
        inst.ctx.globalCompositeOperation = 'source-over';
        inst.ctx.globalAlpha = 1.0;

        const dataUrl  = inst.canvas.toDataURL('image/png');
        const base64   = dataUrl.replace(/^data:image\/png;base64,/, '');
        inst.dotNetRef.invokeMethodAsync(
            'SaveDrawing',
            base64,
            inst.background,
            inst.width,
            inst.height);
    }

    return { attach, detach };
}());
