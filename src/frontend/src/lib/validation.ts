/**
 * Utility to scroll to the first invalid field and focus it.
 */
export function scrollToFirstError(fieldErrors: Record<string, string[]>, containerSelector: string = 'form') {
  if (!fieldErrors || Object.keys(fieldErrors).length === 0) return;

  // 1. Get all keys from fieldErrors and normalize them
  const errorKeys = Object.keys(fieldErrors).map(k => k.toLowerCase().replace(/^\$\./, ''));

  // 2. Find all potential form elements in the container
  const container = document.querySelector(containerSelector) || document.body;
  const elements = Array.from(container.querySelectorAll('input, select, textarea, [data-field]')) as HTMLElement[];

  // 3. Find the first element that matches an error key based on DOM order
  const firstInvalidElement = elements.find(el => {
    const name = el.getAttribute('name')?.toLowerCase();
    const dataField = el.getAttribute('data-field')?.toLowerCase();
    const id = el.id?.toLowerCase();
    
    return errorKeys.some(key => 
      key === name || 
      key === dataField || 
      key === id ||
      (name && (key === name || key.endsWith('.' + name)))
    );
  });

  if (firstInvalidElement) {
    // 4. Determine if we are in a modal or specific container
    const isModal = containerSelector !== 'form' && containerSelector !== 'body';
    
    if (isModal) {
      // For modals, we use scrollIntoView which is more reliable inside overflow containers
      firstInvalidElement.scrollIntoView({
        behavior: 'smooth',
        block: 'center'
      });
    } else {
      // 5. Scroll with offset for sticky header on the main page
      // The sticky header height is 90px (var(--header-height))
      const headerOffset = 150; 
      const elementPosition = firstInvalidElement.getBoundingClientRect().top;
      const offsetPosition = elementPosition + window.pageYOffset - headerOffset;

      window.scrollTo({
        top: offsetPosition,
        behavior: 'smooth'
      });
    }

    // 6. Focus the element after a slight delay
    setTimeout(() => {
      if (['INPUT', 'SELECT', 'TEXTAREA'].includes(firstInvalidElement.tagName)) {
        firstInvalidElement.focus();
      } else {
        const innerInput = firstInvalidElement.querySelector('input, select, textarea') as HTMLElement;
        if (innerInput) {
          innerInput.focus();
        } else {
            firstInvalidElement.setAttribute('tabindex', '-1');
            firstInvalidElement.focus();
        }
      }
    }, 500);
  } else {
    // Fallback: if no specific field element is found, just scroll to top to show the global feedback
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
