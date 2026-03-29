import React from 'react';
import { createPortal } from 'react-dom';

interface DropdownPortalProps {
    children: React.ReactNode;
}

export function DropdownPortal({ children }: DropdownPortalProps) {
    // Render to body
    return createPortal(children, document.body);
}
