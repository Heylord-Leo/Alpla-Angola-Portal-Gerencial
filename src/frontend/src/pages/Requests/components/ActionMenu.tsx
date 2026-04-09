import React, { useState } from 'react';
import { MoreVertical } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

export interface ActionMenuItem {
  label: string;
  icon?: React.ReactNode;
  onClick: () => void;
  variant?: 'default' | 'danger';
}

interface ActionMenuProps {
  items: ActionMenuItem[];
}

export default function ActionMenu({ items }: ActionMenuProps) {
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div className="relative inline-block text-left">
      <button
        onClick={(e) => {
          e.stopPropagation();
          setIsOpen(!isOpen);
        }}
        className="p-2 text-gray-400 hover:text-[#004C90] hover:bg-blue-50 transition-all rounded-sm flex items-center justify-center cursor-pointer"
        aria-label="Ações"
      >
        <MoreVertical size={18} />
      </button>

      <AnimatePresence>
        {isOpen && (
          <>
            <div
              className="fixed inset-0 z-[100]"
              onClick={(e) => {
                  e.stopPropagation();
                  setIsOpen(false);
              }}
            />
            <motion.div
              initial={{ opacity: 0, scale: 0.95, y: -10 }}
              animate={{ opacity: 1, scale: 1, y: 0 }}
              exit={{ opacity: 0, scale: 0.95, y: -10 }}
              className="absolute right-0 mt-1 z-[110] bg-white border-2 border-[#004C90] shadow-xl min-w-[170px] py-1 origin-top-right"
            >
              {items.map((item, index) => (
                <button
                  key={index}
                  onClick={(e) => {
                    e.stopPropagation();
                    item.onClick();
                    setIsOpen(false);
                  }}
                  className={`w-full px-4 py-3 text-[10px] font-black uppercase tracking-widest text-left flex items-center gap-3 transition-colors ${
                    item.variant === 'danger'
                      ? 'text-red-500 hover:bg-red-50'
                      : 'text-[#004C90] hover:bg-blue-50'
                  }`}
                >
                  {item.icon && <span className="flex-shrink-0 w-4 h-4 flex items-center justify-center">{item.icon}</span>}
                  {item.label}
                </button>
              ))}
            </motion.div>
          </>
        )}
      </AnimatePresence>
    </div>
  );
}
