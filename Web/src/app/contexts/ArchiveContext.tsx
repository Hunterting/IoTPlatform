import { createContext, useContext, useState, ReactNode } from 'react';

interface ArchiveContextType {
  selectedAreaForArchive: string | null;
  setSelectedAreaForArchive: (areaName: string | null) => void;
}

const ArchiveContext = createContext<ArchiveContextType | undefined>(undefined);

export function ArchiveProvider({ children }: { children: ReactNode }) {
  const [selectedAreaForArchive, setSelectedAreaForArchive] = useState<string | null>(null);

  return (
    <ArchiveContext.Provider value={{ selectedAreaForArchive, setSelectedAreaForArchive }}>
      {children}
    </ArchiveContext.Provider>
  );
}

export function useArchive() {
  const context = useContext(ArchiveContext);
  if (context === undefined) {
    throw new Error('useArchive must be used within an ArchiveProvider');
  }
  return context;
}
