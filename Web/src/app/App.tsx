import { useState } from 'react';
import { AuthProvider, useAuth } from '@/app/contexts/AuthContext';
import { DictionaryProvider } from '@/app/contexts/DictionaryContext';
import { ThemeProvider, useTheme } from '@/app/contexts/ThemeContext';
import { ArchiveProvider } from '@/app/contexts/ArchiveContext';
import { LoginPage } from '@/app/components/LoginPage';
import { Sidebar, PageType } from '@/app/components/Sidebar';
import { Header } from '@/app/components/Header';
import { DashboardPage } from '@/app/pages/DashboardPage';
import { CustomersPage } from '@/app/pages/CustomersPage';
import { DevicesPage } from '@/app/pages/DevicesPage';
import { AreaManagementPage } from '@/app/pages/AreaManagementPage';
import { AnalyticsPage } from '@/app/pages/AnalyticsPage';
import { AirQualityPage } from '@/app/pages/AirQualityPage';
import { EnvironmentMonitoringPage } from '@/app/pages/EnvironmentMonitoringPage';
import { MonitoringPage } from '@/app/pages/MonitoringPage';
import { ArchivesPage } from '@/app/pages/ArchivesPage';
import { WorkOrderPage } from '@/app/pages/WorkOrderPage';
import { SettingsPage } from '@/app/pages/SettingsPage';
import { UserManagementPage } from '@/app/pages/UserManagementPage';
import { TenantSelectionPage } from '@/app/pages/TenantSelectionPage';
import { LogManagementPage } from '@/app/pages/LogManagementPage';
import { AlertCenterPage } from '@/app/pages/AlertCenterPage';
import { DictionaryManagementPage } from '@/app/pages/DictionaryManagementPage';
import { DataCollectionPage } from '@/app/pages/DataCollectionPage';

function AppContent() {
  const { user, currentCustomer } = useAuth();
  const { themeConfig } = useTheme();
  const [currentPage, setCurrentPage] = useState<PageType>('dashboard');
  const [highlightDeviceId, setHighlightDeviceId] = useState<string | null>(null);
  const [areaFilter, setAreaFilter] = useState<string | null>(null);

  if (!user) {
    return <LoginPage />;
  }

  // Super Admin must select a tenant first
  if (user.role === 'super_admin' && !currentCustomer) {
    return <TenantSelectionPage />;
  }

  const handleNavigateToDevice = (deviceId: string) => {
    setHighlightDeviceId(deviceId);
    setAreaFilter(null);
    setCurrentPage('devices');
  };

  const handleNavigateToArea = (areaName: string) => {
    setAreaFilter(areaName);
    setHighlightDeviceId(null);
    setCurrentPage('devices');
  };

  const renderPage = () => {
    switch (currentPage) {
      case 'dashboard':
        return <DashboardPage onNavigateToArea={handleNavigateToArea} />;
      case 'customers':
        return <CustomersPage />;
      case 'devices':
        return <DevicesPage highlightDeviceId={highlightDeviceId} onClearHighlight={() => setHighlightDeviceId(null)} initialAreaFilter={areaFilter} />;
      case 'area-management':
        return <AreaManagementPage />;
      case 'analytics':
        return <AnalyticsPage />;
      case 'air-quality':
        return <AirQualityPage />;
      case 'environment-monitoring':
        return <EnvironmentMonitoringPage />;
      case 'monitoring':
        return <MonitoringPage />;
      case 'archives':
        return <ArchivesPage onNavigateToDevice={handleNavigateToDevice} />;
      case 'work-orders':
        return <WorkOrderPage />;
      case 'alert-center':
        return <AlertCenterPage />;
      case 'data-collection':
      case 'data-collection-protocol':
      case 'data-collection-gateway':
      case 'data-collection-tunnel':
      case 'data-collection-plugin':
      case 'data-collection-center':
      case 'data-collection-rules':
      case 'data-collection-transform':
      case 'data-collection-database':
      case 'data-collection-export':
        return <DataCollectionPage activePage={currentPage} />;
      case 'logs':
      case 'logs-login':
      case 'logs-operation':
        return <LogManagementPage activePage={currentPage === 'logs' ? 'logs-login' : currentPage} />;
      case 'users':
      case 'users-list':
      case 'users-roles':
        return <UserManagementPage activePage={currentPage} />;
      case 'settings':
      case 'settings-general':
      case 'settings-notifications':
      case 'settings-security':
      case 'settings-devices':
      case 'settings-api':
      case 'settings-dictionary':
        return <SettingsPage activePage={currentPage} />;
      case 'dictionary':
        return <DictionaryManagementPage />;
      default:
        return <DashboardPage onNavigateToArea={handleNavigateToArea} />;
    }
  };

  return (
    <div className={`min-h-screen bg-gradient-to-br ${themeConfig.colors.bgPrimary} flex`}
      style={currentPage === 'dashboard' ? { height: '100vh', overflow: 'hidden' } : {}}>
      <Sidebar
        currentPage={currentPage}
        onNavigate={(page) => {
          // Clear area filter when manually navigating (not from dashboard zone click)
          if (page === 'devices') setAreaFilter(null);
          setCurrentPage(page);
        }}
        userRole={user.role}
      />
      <div className="flex-1 flex flex-col min-h-0">
        {currentPage !== 'dashboard' && <Header currentPage={currentPage} />}
        <main className={`flex-1 ${currentPage === 'dashboard' ? 'overflow-hidden' : 'overflow-y-auto'}`}>{renderPage()}</main>
      </div>
    </div>
  );
}

import { AreaProvider } from '@/app/contexts/AreaContext';
import { DeviceProvider } from '@/app/contexts/DeviceContext';

export default function App() {
  return (
    <AuthProvider>
      <AreaProvider>
        <DeviceProvider>
          <DictionaryProvider>
            <ArchiveProvider>
              <ThemeProvider>
                <AppContent />
              </ThemeProvider>
            </ArchiveProvider>
          </DictionaryProvider>
        </DeviceProvider>
      </AreaProvider>
    </AuthProvider>
  );
}