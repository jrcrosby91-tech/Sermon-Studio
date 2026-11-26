// Browser outline workspace. No remote AI requests: the original generator is always available.
const byId = id => document.getElementById(id);
let outlineTimer;
function activeSermon() {
  const title = byId('title').value || 'Untitled sermon';
  const date = byId('date').value;
  const text = byId('editor').value || '';
  const archive = JSON.parse(localStorage.getItem('sermonArchive') || '[]');
  const match = archive.find(item => item.title === title && item.date === date);
  return {title,date,text,match,archive};
}
function showOutline() {
  const {text,match} = activeSermon();
  byId('outlineTranscript').textContent = text || 'Open a transcript in the editor first.';
  byId('outlineText').value = match?.outline || sessionStorage.getItem('sermonOutlineDraft') || '';
  byId('outlineSaved').textContent = match ? 'Saved sermon' : 'Save the sermon to enable outline autosave';
}
function saveOutline() {
  const value = byId('outlineText').value;
  sessionStorage.setItem('sermonOutlineDraft', value);
  const {match,archive} = activeSermon();
  if (!match) {byId('outlineSaved').textContent='Draft only — save sermon in Transcript editor';return}
  match.outline=value;
  try {
    localStorage.setItem('sermonArchive', JSON.stringify(archive));
    byId('outlineSaved').textContent='Outline saved on this device';
  } catch (error) {byId('outlineSaved').textContent='Could not save: '+error.message}
}
function originalOutline(text,title) {
  const sentences = text.replace(/\s+/g,' ').split(/[.!?]+/).map(s=>s.trim()).filter(s=>s.length>18);
  if (!sentences.length) return '';
  const count=Math.min(5,sentences.length), size=Math.ceil(sentences.length/count);
  const sections=Array.from({length:count},(_,i)=>{
    const part=sentences.slice(i*size,(i+1)*size);
    return part.length ? (i+1)+'. '+part[0].slice(0,100)+'\n   Supporting excerpt: '+part.slice(1,3).join('. ').slice(0,300) : '';
  }).filter(Boolean);
  const refs=[...new Set((text.match(/\b(?:Genesis|Exodus|Psalms?|Proverbs|Isaiah|Jeremiah|Matthew|Mark|Luke|John|Acts|Romans|(?:[1-3] )?Corinthians|Galatians|Ephesians|Philippians|Colossians|Hebrews|James|Revelation)\s+\d+(?::\d+(?:[-–]\d+)?)?/gi)||[]))];
  return ['SERMON OUTLINE (REVIEW DRAFT)','Title: '+title,'Central message: [Review the sermon and add its central theme]','','INTRODUCTION',sentences.slice(0,2).join('. ').slice(0,450),'','MAIN POINTS',...sections,'','SCRIPTURE REFERENCES',...(refs.length?refs:['[No explicit references detected]']),'','APPLICATION','[Add practical applications from the sermon]','','CONCLUSION',sentences.slice(-2).join('. ').slice(0,450),'','Note: Transcript-based draft; verify all headings and references.'].join('\n');
}
function exportFile(ext,content,mime) {
  const filename=(byId('title').value||'Sermon').replace(/[\\/:*?"<>|]/g,'_')+'-Outline.'+ext;
  if(typeof window.studioExport==='function') window.studioExport(filename,content,mime);
  else {const a=document.createElement('a'),url=URL.createObjectURL(new Blob([content],{type:mime}));a.href=url;a.download=filename;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000)}
}
byId('makeOutline').onclick=()=>{
  if(byId('outlineEngine').value==='ollama') {byId('outlineHint').textContent='Ollama runs in the native desktop apps. Browsers cannot reliably access a local Ollama service without explicit server configuration.';return}
  const {text,title}=activeSermon();
  if(!text.trim()) {byId('outlineHint').textContent='Open or enter a sermon transcript first.';return}
  byId('outlineText').value=originalOutline(text,title);
  byId('outlineHint').textContent='Original outline generated locally. Review and edit before sharing.';
  saveOutline();
};
byId('outlineText').addEventListener('input',()=>{byId('outlineSaved').textContent='Saving…';clearTimeout(outlineTimer);outlineTimer=setTimeout(saveOutline,500)});
byId('copyOutline').onclick=async()=>{try{await navigator.clipboard.writeText(byId('outlineText').value);byId('outlineHint').textContent='Outline copied.'}catch{byId('outlineHint').textContent='Clipboard unavailable; select the text to copy.'}};
byId('outlineLocate').onclick=()=>{
  const query=byId('outlineFind').value.trim(),text=byId('outlineTranscript').textContent;
  if(!query)return;
  const index=text.toLowerCase().indexOf(query.toLowerCase());
  if(index<0){byId('outlineHint').textContent='Phrase not found. Try a shorter excerpt.';return}
  byId('outlineHint').textContent='Phrase found in transcript.';
  const pre=byId('outlineTranscript');pre.focus();pre.scrollTop=(index/Math.max(text.length,1))*pre.scrollHeight;
};
byId('outlineMarkdown').onclick=()=>exportFile('md',byId('outlineText').value,'text/markdown');
byId('outlineWord').onclick=()=>{
  const escaped=byId('outlineText').value.replace(/[\\{}]/g,m=>'\\'+m).replace(/\n/g,'\\par ');
  exportFile('rtf','{\\rtf1\\ansi '+escaped+'}','application/rtf');
};
byId('outlinePDF').onclick=()=>{
  const win=window.open('','_blank');if(!win){byId('outlineHint').textContent='Allow popups to print the outline.';return}
  const heading=win.document.createElement('h1'),pre=win.document.createElement('pre');
  heading.textContent=byId('title').value||'Sermon Outline';pre.textContent=byId('outlineText').value;
  pre.style.whiteSpace='pre-wrap';pre.style.font='14px Georgia,serif';win.document.body.append(heading,pre);win.print();
};
document.querySelectorAll('[data-page="outlines"]').forEach(button=>button.addEventListener('click',()=>setTimeout(showOutline,0)));
byId('save').addEventListener('click',()=>setTimeout(saveOutline,0));
