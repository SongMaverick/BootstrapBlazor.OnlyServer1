// 订单管理页自适应左右栏和拖拽分隔条。
// 实际内容宽度足够时按比例分栏；空间不足以同时满足两栏最小宽度时自动上下堆叠。

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
        const minL = Math.max(0, Number(minLeftPx) || 0);
        const minR = Math.max(0, Number(minRightPx) || 0);

        let dragging = false;
        let leftRatio = 0.42;
        let startX = 0;
        let startLeftWidth = 0;
        let startRightWidth = 0;

        function clearSplitLayout() {
            left.parentElement.style.flexWrap = '';
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
        }

        function setSplitWidths(leftPx, rightPx) {
            left.parentElement.style.flexWrap = 'nowrap';
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

        function applyLayout() {
            const parentWidth = left.parentElement.clientWidth;
            const availableWidth = Math.max(0, parentWidth - HANDLE_WIDTH - 2);
            if (availableWidth < minL + minR) {
                clearSplitLayout();
                return;
            }

            const preferredLeft = Math.round(availableWidth * leftRatio);
            const leftPx = Math.min(Math.max(preferredLeft, minL), availableWidth - minR);
            const rightPx = availableWidth - leftPx;
            setSplitWidths(leftPx, rightPx);
        }
        applyLayout();

        window.addEventListener('resize', applyLayout);
        if (window.ResizeObserver) {
            const observer = new ResizeObserver(applyLayout);
            observer.observe(left.parentElement);
        }

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
            setSplitWidths(newLeft, newRight);
            leftRatio = newLeft / (newLeft + newRight);
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
            if (handle.style.display === 'none') return;
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