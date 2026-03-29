import { useState, useEffect, useCallback, RefObject } from 'react';

export interface DropdownPosition {
    top: number;
    left: number;
    width: number;
    maxHeight: number;
    placement: 'bottom' | 'top';
}

function getScrollParents(node: HTMLElement | null): HTMLElement[] {
    const parents: HTMLElement[] = [];
    let curr = node;
    while (curr && curr !== document.body) {
        const style = window.getComputedStyle(curr);
        const overflow = style.getPropertyValue('overflow');
        const overflowY = style.getPropertyValue('overflow-y');
        const overflowX = style.getPropertyValue('overflow-x');
        
        if (
            /(auto|scroll)/.test(overflow) || 
            /(auto|scroll)/.test(overflowY) || 
            /(auto|scroll)/.test(overflowX)
        ) {
            parents.push(curr);
        }
        curr = curr.parentElement;
    }
    parents.push(window as any); // Add window as the ultimate scroll parent
    return parents;
}

export interface DropdownPositionStyle extends React.CSSProperties {
    position: 'fixed';
    top?: number | string;
    bottom?: number | string;
    left: number;
    width: number;
    maxHeight: number;
    zIndex: number;
}

export function useDropdownPosition(
    triggerRef: RefObject<HTMLElement>, 
    isOpen: boolean,
    minPanelHeight: number = 300,
    minPanelWidth: number = 400
) {
    const [style, setStyle] = useState<DropdownPositionStyle | null>(null);

    const updatePosition = useCallback(() => {
        if (!triggerRef.current || !isOpen) return;

        const rect = triggerRef.current.getBoundingClientRect();
        const viewportHeight = window.innerHeight;
        const viewportWidth = window.innerWidth;
        
        const spaceBelow = viewportHeight - rect.bottom;
        const spaceAbove = rect.top;
        
        let placement: 'bottom' | 'top' = 'bottom';
        let maxHeight = spaceBelow - 20;

        if (spaceBelow < minPanelHeight && spaceAbove > spaceBelow) {
            placement = 'top';
            maxHeight = spaceAbove - 20;
        }

        const width = Math.max(rect.width, minPanelWidth);
        let left = rect.left;
        
        // Horizontal viewport safety
        if (left + width > viewportWidth) {
            left = Math.max(20, viewportWidth - width - 20);
        }

        const newStyle: DropdownPositionStyle = {
            position: 'fixed',
            left: left,
            width: width,
            maxHeight: Math.max(maxHeight, 100),
            zIndex: 1000,
            marginTop: placement === 'bottom' ? '6px' : '0',
            marginBottom: placement === 'top' ? '6px' : '0',
        };

        if (placement === 'bottom') {
            newStyle.top = rect.bottom;
        } else {
            newStyle.bottom = viewportHeight - rect.top;
        }

        setStyle(newStyle);
    }, [triggerRef, isOpen, minPanelHeight, minPanelWidth]);

    useEffect(() => {
        if (!isOpen) return;

        updatePosition();
        
        const scrollParents = getScrollParents(triggerRef.current);
        
        const handleEvent = () => updatePosition();
        
        window.addEventListener('resize', handleEvent);
        scrollParents.forEach(p => p.addEventListener('scroll', handleEvent));
        
        return () => {
            window.removeEventListener('resize', handleEvent);
            scrollParents.forEach(p => p.removeEventListener('scroll', handleEvent));
        };
    }, [isOpen, updatePosition, triggerRef]);

    return style;
}
