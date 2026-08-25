import React from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import SupplierFichaDetailContent from './SupplierFichaDetailContent';

// Phase 3D / Layer C — thin route host for /contracts/fichas/:id. It owns only the route concerns (reading
// the id param and list navigation) and delegates the entire Supplier Ficha UI to the reusable
// SupplierFichaDetailContent. The future Buyer Workspace drawer hosts the SAME content with hostMode="drawer";
// there is no second Supplier form. Page behavior is unchanged.
const SupplierFichaDetail: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  return (
    <SupplierFichaDetailContent
      supplierId={Number(id)}
      hostMode="page"
      onClose={() => navigate('/contracts/fichas')}
    />
  );
};

export default SupplierFichaDetail;
