import React from 'react';
import { ChevronRight } from 'lucide-react';
import { Link } from 'react-router-dom';

export interface BreadcrumbItem {
    label: string;
    to?: string; // If omitted, rendered as plain text (current page)
}

export interface BreadcrumbProps {
    items: BreadcrumbItem[];
    className?: string;
    style?: React.CSSProperties;
}

export function Breadcrumb({ items, className, style }: BreadcrumbProps) {
    return (
        <nav
            aria-label="Breadcrumb"
            className={className}
            style={{
                display: 'flex',
                alignItems: 'center',
                gap: '6px',
                fontSize: '0.82rem',
                color: 'var(--color-text-muted)',
                marginBottom: '16px',
                flexWrap: 'wrap',
                ...style
            }}
        >
            {items.map((item, index) => {
                const isLast = index === items.length - 1;
                return (
                    <React.Fragment key={index}>
                        {index > 0 && (
                            <ChevronRight
                                size={14}
                                style={{ color: 'var(--color-text-muted)', opacity: 0.5, flexShrink: 0 }}
                            />
                        )}
                        {item.to && !isLast ? (
                            <Link
                                to={item.to}
                                style={{
                                    color: 'var(--color-primary)',
                                    textDecoration: 'none',
                                    fontWeight: 500,
                                    transition: 'opacity 0.15s ease',
                                }}
                                onMouseEnter={(e) => { (e.target as HTMLElement).style.opacity = '0.7'; }}
                                onMouseLeave={(e) => { (e.target as HTMLElement).style.opacity = '1'; }}
                            >
                                {item.label}
                            </Link>
                        ) : (
                            <span style={{
                                color: isLast ? 'var(--color-text-main)' : 'var(--color-text-muted)',
                                fontWeight: isLast ? 600 : 400,
                            }}>
                                {item.label}
                            </span>
                        )}
                    </React.Fragment>
                );
            })}
        </nav>
    );
}
