// (2026-09-04) 订单管理页左右两栏拖拽分隔条
//   - 完全由 JS 接管布局:不依赖任何 CSS class(包括 col-lg-4/8 或自定义 class),
//     即使 app.css 没刷新加载,布局也能正确(JS 设 inline flex/width/maxWidth 强制覆盖)
//   - 屏宽 ≥ 992px → 左右布局(splitter 6px + 左侧 1/3 + 右侧 2/3)
//   - 屏宽 < 992px → 清空 inline,退回 Bootstrap col-12 全宽堆叠
//   - 监听 window.resize,跨屏宽边界时自动重设
//   - attach 幂等(handle.dataset.bound === '1' 直接 return),避免重复绑事件
//   - 不持久化宽度:刷新回到 1/3 / 2/3 默认值,后续要 LocalStorage 再叠

window.orderMgmtResizer = {
    /**
     * @param {string} leftId
     * @param {string} rightId
     * @param {string} handleId
     * @param {number} minLeftPx
     * @param {number} minRightPx
     */
    attach: function (leftId, rightId, handleId, minLeftPx, minRightPx) {
        const left = document.getElementById(leftId);
        const right = document.getElementById(rightId);
        const handle = document.getElementById(handleId);
        if (!left || !right || !handle) return;
        if (handle.dataset.bound === '1') return;
        handle.dataset.bound = '1';

        const HANDLE_WIDTH = 6;
        const LG_BREAKPOINT = 992;
        const minL = Math.max(0, Number(minLeftPx) || 0);
        const minR = Math.max(0, Number(minRightPx) || 0);

        let dragging = false;
        let startX = 0;
        let startLeftWidth = 0;
        let startRightWidth = 0;

        // 大屏设布局,小屏清 inline 退回堆叠
        function applyLayout() {
            if (window.innerWidth < LG_BREAKPOINT) {
                left.style.flex = '';
                left.style.maxWidth = '';
                left.style.width = '';
                right.style.flex = '';
                right.style.maxWidth = '';
                right.style.width = '';
                handle.style.flex = '';
                handle.style.maxWidth = '';
                handle.style.width = '';
                handle.style.display = 'none';
                return;
            }
            // 大屏:1/3 + 2/3,splitter 6px(总宽 = left + handle + right = parent)
            const parentWidth = left.parentElement.getBoundingClientRect().width;
            const leftPx = Math.round((parentWidth - HANDLE_WIDTH) * (1 / 3));
            const rightPx = parentWidth - HANDLE_WIDTH - leftPx;
            left.style.flex = '0 0 ' + leftPx + 'px';
            left.style.maxWidth = leftPx + 'px';
            left.style.width = leftPx + 'px';
            right.style.flex = '0 0 ' + rightPx + 'px';
            right.style.maxWidth = rightPx + 'px';
            right.style.width = rightPx + 'px';
            handle.style.flex = '0 0 ' + HANDLE_WIDTH + 'px';
            handle.style.maxWidth = HANDLE_WIDTH + 'px';
            handle.style.width = HANDLE_WIDTH + 'px';
            handle.style.display = 'block';
            handle.style.cursor = 'col-resize';
            handle.style.position = 'relative';
            handle.style.zIndex = '2';
            handle.style.userSelect = 'none';
        }
        applyLayout();

        // 跨屏宽边界时重新布局(避免小屏残留 inline 宽度导致堆叠失败)
        let lastLarge = window.innerWidth >= LG_BREAKPOINT;
        window.addEventListener('resize', function () {
            const nowLarge = window.innerWidth >= LG_BREAKPOINT;
            if (nowLarge !== lastLarge) {
                applyLayout();
                lastLarge = nowLarge;
            }
        });

        const onMove = function (e) {
            if (!dragging) return;
            const dx = e.clientX - startX;
            let newLeft = startLeftWidth + dx;
            let newRight = startRightWidth - dx;
            if (newLeft < minL) {
                newLeft = minL;
                newRight = startLeftWidth + startRightWidth - newLeft;
            }
            if (newRight < minR) {
                newRight = minR;
                newLeft = startLeftWidth + startRightWidth - newRight;
            }
            left.style.flex = '0 0 ' + newLeft + 'px';
            left.style.maxWidth = newLeft + 'px';
            left.style.width = newLeft + 'px';
            right.style.flex = '0 0 ' + newRight + 'px';
            right.style.maxWidth = newRight + 'px';
            right.style.width = newRight + 'px';
        };
        const onUp = function () {
            if (!dragging) return;
            dragging = false;
            handle.classList.remove('dragging');
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
        };

        handle.addEventListener('mousedown', function (e) {
            // 小屏不响应拖动(布局已退回堆叠,splitter display:none,理论上点不到;兜底)
            if (window.innerWidth < LG_BREAKPOINT) return;
            e.preventDefault();
            dragging = true;
            startX = e.clientX;
            startLeftWidth = left.getBoundingClientRect().width;
            startRightWidth = right.getBoundingClientRect().width;
            handle.classList.add('dragging');
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
        });
    }
};