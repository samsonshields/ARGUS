window.argusBackupDownload = async function () {
    const response = await fetch('/downloads/backups', { cache: 'no-store' });
    if (!response.ok) throw new Error('Backup export failed.');
    const blob = await response.blob();
    const filename = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(response.headers.get('Content-Disposition') || '')?.[1] || 'argus-workspace-backup.zip';
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a'); link.href = url; link.download = decodeURIComponent(filename); link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
