const fs = require('fs');
const path = require('path');

const walkSync = (dir, filelist = []) => {
  fs.readdirSync(dir).forEach(file => {
    const dirFile = path.join(dir, file);
    try {
      filelist = walkSync(dirFile, filelist);
    } catch (err) {
      if (err.code === 'ENOTDIR' || err.code === 'EBADF') filelist.push(dirFile);
    }
  });
  return filelist;
};

const excludeFiles = ['RequestAttachments.tsx', 'tokens.css', 'globals.css'];
const targetDirs = [
  'c:/dev/alpla-portal/src/frontend/src/components',
  'c:/dev/alpla-portal/src/frontend/src/pages',
  'c:/dev/alpla-portal/src/frontend/src/features'
];

let files = [];
targetDirs.forEach(d => {
  if (fs.existsSync(d)) {
    files = files.concat(walkSync(d).filter(f => f.endsWith('.tsx') && !excludeFiles.some(ex => f.endsWith(ex))));
  }
});

let modifiedFiles = 0;

files.forEach(f => {
  let content = fs.readFileSync(f, 'utf8');
  let originalCode = content;

  // Replace background whites
  content = content.replace(/background:\s*'#f{3,6}'/gi, "background: 'var(--color-bg-surface)'");
  content = content.replace(/backgroundColor:\s*'#f{3,6}'/gi, "backgroundColor: 'var(--color-bg-surface)'");
  content = content.replace(/background:\s*\"#f{3,6}\"/gi, "background: 'var(--color-bg-surface)'");
  
  // Replace specific off-whites
  content = content.replace(/background:\s*'#f[389]f[4a]f[bc6]'/gi, "background: 'var(--color-bg-page)'");
  content = content.replace(/backgroundColor:\s*'#f[389]f[4a]f[bc6]'/gi, "backgroundColor: 'var(--color-bg-page)'");

  // Replace border colors (like #e5e7eb, #d1d5db)
  content = content.replace(/border(.*?):\s*'(.*?solid )#[edcb][15][d7][5e][de][b1]'/gi, "border$1: '$2var(--color-border)'");

  if (content !== originalCode) {
    fs.writeFileSync(f, content, 'utf8');
    modifiedFiles++;
    console.log('Modified:', f);
  }
});

console.log('Total files modified:', modifiedFiles);
