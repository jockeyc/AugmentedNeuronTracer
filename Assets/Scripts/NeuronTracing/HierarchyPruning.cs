using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class HierarchyPruning
{
    public class HierarchySegment
    {
        public HierarchySegment parent;
        public Marker leafMarker;
        public Marker root_marker;
        public double length;
        public int level;

        public HierarchySegment()
        {
            leafMarker = null;
            root_marker = null;
            length = 0;
            level = 1;
            parent = null;
        }

        public void setValue(Marker _leaf, Marker _root, double _len, int _level)
        {
            leafMarker = _leaf;
            root_marker = _root;
            length = _len;
            level = _level;
            parent = null;
        }

        public List<Marker> get_markers()
        {
            Marker p = leafMarker;
            List<Marker> markers = new();
            while (p != root_marker)
            {
                markers.Add(p);
                p = p.parent;
            }
            markers.Add(root_marker);
            return markers;
        }
    }
    List<HierarchySegment> swc2topoSegs(List<Marker> inswc, byte[] img, int sz0, int sz1, int sz2)
    {
        int tolNum = inswc.Count;
        Dictionary<Marker, int> swcMap = new();
        for (int i = 0; i < tolNum; i++)
        {
            swcMap[inswc[i]] = i;
        }

        int[] childs_num = new int[tolNum];

        for (int i = 0; i < tolNum; i++)
        {
            if (inswc[i].parent != null && !swcMap.ContainsKey(inswc[i].parent))
            {
                inswc[i].parent = null;
            }
            if (inswc[i].parent == null) continue;
            int parent_index = swcMap[inswc[i].parent];
            childs_num[parent_index]++;
        }

        List<Marker> leafMarkers = new();
        for (int i = 0; i < tolNum; i++)
        {
            if (childs_num[i] == 0) leafMarkers.Add(inswc[i]);
        }

        int leafNum = leafMarkers.Count;

        int sz01 = sz0 * sz1;

        double[] topoDists = new double[tolNum];

        Marker[] topoLeafs = new Marker[tolNum];

        //calculate distance
        for (int i = 0; i < leafNum; i++)
        {
            Marker leafMarker = leafMarkers[i];
            Marker childNode = leafMarkers[i];
            Marker parentNode = childNode.parent;
            int child_index = swcMap[childNode];
            topoLeafs[child_index] = leafMarker;
            topoDists[child_index] = img[leafMarker.img_index(sz0, sz01)] / 255.0;
            //topoDists[child_index] = 0;

            while (parentNode != null)
            {
                int parent_index = swcMap[parentNode];
                double tmp_dst = (img[parentNode.img_index(sz0, sz01)]) / 255.0 + topoDists[child_index];
                //double tmp_dst = Vector3.Distance(parentNode.position,childNode.position) + topoDists[child_index];
                if (tmp_dst > topoDists[parent_index])
                {
                    topoDists[parent_index] = tmp_dst;
                    topoLeafs[parent_index] = topoLeafs[child_index];
                }
                else break;
                child_index = parent_index;
                parentNode = parentNode.parent;
            }
        }

        //activate hierarchy segments
        Dictionary<Marker, int> leafMap = new();
        List<HierarchySegment> topoSegs = new();
        for (int i = 0; i < leafNum; i++)
        {
            topoSegs.Add(new HierarchySegment());
            leafMap[leafMarkers[i]] = i;
        }

        for (int i = 0; i < leafNum; i++)
        {
            Marker leafMarker = leafMarkers[i];
            Marker rootMarker = leafMarker;
            Marker rootParent = rootMarker.parent;
            int level = 1;
            while (rootParent != null && topoLeafs[swcMap[rootParent]] == leafMarker)
            {
                if (childs_num[swcMap[rootMarker]] >= 2) level++;
                rootMarker = rootParent;
                rootParent = rootMarker.parent;
            }

            double dst = topoDists[swcMap[rootMarker]];

            topoSegs[i].setValue(leafMarker, rootMarker, dst, level);


            if (rootParent == null)
            {
                topoSegs[i].parent = null;
            }
            else
            {
                Marker leaf_marker2 = topoLeafs[swcMap[rootParent]];
                if (leaf_marker2 != null)
                {
                    int leaf_index2 = leafMap[leaf_marker2];
                    topoSegs[i].parent = topoSegs[leaf_index2];
                    //... rest of the code
                }
            }
        }

        return topoSegs;
    }

     List<Marker> TopoSegs2swc(List<HierarchySegment> topoSegs, int swcType)
    {
        var outswc = new List<Marker>();
        double min_dst = double.MaxValue;
        double max_dst = double.MinValue;
        int min_level = int.MaxValue;
        int max_level = int.MinValue;
        foreach (HierarchySegment topo_seg in topoSegs)
        {
            double dst = topo_seg.length;
            min_dst = Math.Min(dst, min_dst);
            max_dst = Math.Max(dst, max_dst);
            int level = topo_seg.level;
            min_level = Math.Min(level, min_level);
            max_level = Math.Max(level, max_level);
        }

        max_level = Math.Min(max_level, 20);

        max_dst -= min_dst;
        if (max_dst == 0) max_dst = 0.0000001;
        max_level -= min_level;
        if (max_level == 0) max_level = 1;
        foreach (HierarchySegment topo_seg in topoSegs)
        {
            double dst = topo_seg.length;
            int level = Math.Min(topo_seg.level, max_level);

            int color_id = (int)((swcType == 0) ? (dst - min_dst) / max_dst * 254 + 20.5 : (level - min_level) / max_level * 254.0 + 20.5);
            List<Marker> tmp_markers;
            tmp_markers = topo_seg.get_markers();
            foreach (Marker marker in tmp_markers)
            {
                //marker.type = color_id;
            }
            outswc.AddRange(tmp_markers);
        }
        return outswc;
    }

    void topo_segs2swc(HashSet<HierarchySegment> out_segs, List<HierarchySegment> filtered_segs, out List<Marker> outswc, int swc_type)
    {
        outswc = new List<Marker>();
        foreach (HierarchySegment topo_seg in filtered_segs)
        {
            int color_id = out_segs.Contains(topo_seg) ? 0 : 1;
            List<Marker> tmp_markers;
            tmp_markers = topo_seg.get_markers();
            foreach (Marker marker in tmp_markers)
            {
                //marker.type = color_id;
            }
            outswc.AddRange(tmp_markers);
        }
    }

    public List<Marker> HierarchyPrune(List<Marker> inswc, byte[] img, int sz0, int sz1, int sz2, ref float somaRadius, double bkg_thresh = 30.0, double length_thresh = 5.0, bool isSoma = true, double SR_ratio = 1.0 / 9.0, float lengthFactor = 4, float lengthThreshold = 0.5f)
    {
        int sz01 = sz0 * sz1;
        int tol_sz = sz01 * sz2;

        List<HierarchySegment> topoSegs  = swc2topoSegs(inswc, img, sz0, sz1, sz2);
        //Debug.Log(topoSegs.Count);

        List<HierarchySegment> filterSegs = new();
        Marker root = inswc.FirstOrDefault(marker => marker.parent == null);

        double real_thresh = Math.Max(10, bkg_thresh);

        if (somaRadius < 0) somaRadius = Marker.markerRadius(img, sz0, sz1, sz2, root, real_thresh);
        Debug.Log($"Soma Radius: {somaRadius}");

        //somaRadius = MathF.Min(10,somaRadius);

        //filterSegs.AddRange(topoSegs.Where(topoSeg => topoSeg.length >= 2));
        foreach (HierarchySegment topoSeg in topoSegs)
        {
            Marker leafMarker = topoSeg.leafMarker;
            if (Vector3.Distance(leafMarker.position, root.position) < 3 * somaRadius)
            {
                if (topoSeg.length >= somaRadius * lengthFactor)
                {
                    filterSegs.Add(topoSeg);
                }
            }
            else
            {
                if (topoSeg.length >= lengthThreshold)
                {
                    filterSegs.Add(topoSeg);
                }
            }
        }

        //Debug.Log(filterSegs.Count);

        //calculate radius of every node
        foreach (var seg in filterSegs)
        {
            Marker leaf_marker = seg.leafMarker;
            Marker root_marker = seg.root_marker;
            Marker p = leaf_marker;
            while (p != root_marker.parent)
            {
                p.radius = MathF.Max(p.radius,1.5f);
                //p.radius = Marker.markerRadius(img, sz0, sz1, sz2, p, real_thresh);
                p.radius = MathF.Min(somaRadius / 6, Marker.markerRadius(img, sz0, sz1, sz2, p, real_thresh));
                p = p.parent;
            }
        }
        root.radius = somaRadius;
        //Debug.Log("calculate radius done");

        //hierarchy pruning
        byte[] tmpimg = new byte[img.Length];
        img.CopyTo(tmpimg, 0);

        filterSegs.Sort((a, b) => -a.length.CompareTo(b.length));

        List<HierarchySegment> outSegs = new();
        double tolSumSig = 0.0, tolSumRdc = 0.0;
        HashSet<HierarchySegment> visitedSegs = new();

        foreach (var seg in filterSegs)
        {
            if (seg.parent != null && !visitedSegs.Contains(seg.parent)) continue;
            Marker leaf_marker = seg.leafMarker;
            Marker root_marker = seg.root_marker;

            double sum_sig = 0;
            double sum_rdc = 0;

            Marker p = leaf_marker;
            while (p != root_marker.parent)
            {
                if (tmpimg[p.img_index(sz0, sz01)] == 0)
                {
                    sum_rdc += img[p.img_index(sz0, sz01)];
                }
                else
                {
                    int r = (int)p.radius;
                    int x = (int)(p.position.x);
                    int y = (int)(p.position.y);
                    int z = (int)(p.position.z);
                    double sum_sphere_size = 0;
                    double sum_delete_size = 0;
                    for (int ii = -r; ii <= r; ii++)
                    {
                        int x2 = x + ii;
                        if (x2 < 0 || x2 >= sz0) continue;
                        for (int jj = -r; jj <= r; jj++)
                        {
                            int y2 = y + jj;
                            if (y2 < 0 || y2 >= sz1) continue;
                            for (int kk = -r; kk <= r; kk++)
                            {
                                int z2 = z + kk;
                                if (z2 < 0 || z2 >= sz2) continue;
                                if (ii * ii + jj * jj + kk * kk > r * r) continue;
                                int index = z2 * sz01 + y2 * sz0 + x2;
                                sum_sphere_size++;
                                if (tmpimg[index] != img[index])
                                {
                                    sum_delete_size++;
                                }
                            }
                        }
                    }

                    if (sum_sphere_size > 0 && sum_delete_size / sum_sphere_size > 0.1)
                    {
                        sum_rdc += img[p.img_index(sz0, sz01)];
                    }
                    else sum_sig += img[p.img_index(sz0, sz01)];
                }
                p = p.parent;
            }

            //if (seg.parent == null || sum_rdc == 0 || (sum_sig / sum_rdc >= SR_ratio && sum_sig >= byte.MaxValue))
            if (seg.parent == null || sum_rdc == 0 || (sum_sig / sum_rdc >= SR_ratio))
            {
                tolSumSig += sum_sig;
                tolSumRdc += sum_rdc;
                List<Marker> seg_markers = new();
                p = leaf_marker;
                while (p != root_marker)
                {
                    if (tmpimg[p.img_index(sz0, sz01)] != 0)
                    {
                        seg_markers.Add(p);
                    }
                    p = p.parent;
                }

                foreach (var marker in seg_markers)
                {
                    p = marker;
                    int r = (int)p.radius;
                    if (r > 0)
                    {
                        int x = (int)(p.position.x + 0.5);
                        int y = (int)(p.position.y + 0.5);
                        int z = (int)(p.position.z + 0.5);
                        //double sum_sphere_size = 0;
                        //double sum_delete_size = 0;
                        for (int ii = -r; ii <= r; ii++)
                        {
                            int x2 = x + ii;
                            if (x2 < 0 || x2 >= sz0) continue;
                            for (int jj = -r; jj <= r; jj++)
                            {
                                int y2 = y + jj;
                                if (y2 < 0 || y2 >= sz1) continue;
                                for (int kk = -r; kk <= r; kk++)
                                {
                                    int z2 = z + kk;
                                    if (z2 < 0 || z2 >= sz2) continue;
                                    if (ii * ii + jj * jj + kk * kk > r * r) continue;
                                    int index = z2 * sz01 + y2 * sz0 + x2;
                                    tmpimg[index] = 0;
                                }

                            }
                        }
                    }
                }

                outSegs.Add(seg);
                visitedSegs.Add(seg);
            }
        }

        //evaluation
        double tree_sig = 0;
        double covered_sig = 0;
        foreach (var m in inswc)
        {
            tree_sig += img[m.img_index(sz0, sz01)];
            if (tmpimg[m.img_index(sz0, sz01)] == 0) covered_sig += img[m.img_index(sz0, sz01)];
        }
 
        //Debug.Log("S/T ratio" + covered_sig / tree_sig + "(" + covered_sig + "/" + tree_sig + ")");
        //Debug.Log(outSegs.Count);

        var outswc = TopoSegs2swc(outSegs, 0);
        return outswc;
        //topo_segs2swc(visited_segs,filter_segs, out outswc, 0);
    }

    //public List<Marker> hierarchy_prune_repair(List<Marker> inswc, byte[] img, int sz0, int sz1, int sz2, double bkg_thresh = 30.0, double length_thresh = 5.0, double SR_ratio = 1.0 / 9.0)
    //{
    //    int sz01 = sz0 * sz1;
    //    int tol_sz = sz01 * sz2;

    //    List<HierarchySegment> topo_segs = swc2topoSegs(inswc, img, sz0, sz1, sz2);
    //    Debug.Log(topo_segs.Count);

    //    List<HierarchySegment> filter_segs = new List<HierarchySegment>();
    //    Marker root = new Marker();
    //    foreach (Marker marker in inswc)
    //    {
    //        if (marker.parent == null)
    //        {
    //            root = marker;
    //            break;
    //        }
    //    }

    //    double real_thresh = bkg_thresh;
    //    //double real_thresh = 50;
    //    //real_thresh = Math.Max(real_thresh, bkg_thresh);

    //    foreach (HierarchySegment topo_seg in topo_segs)
    //    {
    //        Marker leaf_marker = topo_seg.leafMarker;
    //        if (topo_seg.length >= length_thresh)
    //        {
    //            filter_segs.Add(topo_seg);
    //        }
    //    }

    //    Debug.Log(filter_segs.Count);
    //    if (filter_segs.Count == 0)
    //    {
    //        filter_segs = topo_segs;
    //    }


    //    //calculate radius of every node
    //    foreach (var seg in filter_segs)
    //    {
    //        Marker leaf_marker = seg.leafMarker;
    //        Marker root_marker = seg.root_marker;
    //        Marker p = leaf_marker;
    //        while (p != root_marker.parent)
    //        {
    //            p.radius = Marker.markerRadius(img, sz0, sz1, sz2, p, real_thresh);
    //            p = p.parent;
    //        }
    //    }
    //    Debug.Log("calculate radius done");

    //    //hierarchy pruning
    //    byte[] tmpimg = new byte[img.Length];
    //    img.CopyTo(tmpimg, 0);

    //    filter_segs.Sort((a, b) => { return -a.length.CompareTo(b.length); });

    //    var out_segs = new List<HierarchySegment>();
    //    double tol_sum_sig = 0.0, tol_sum_rdc = 0.0;
    //    var visited_segs = new HashSet<HierarchySegment>();
    //    int count = 0;

    //    foreach (var seg in filter_segs)
    //    {
    //        if (seg.parent != null && !visited_segs.Contains(seg.parent)) continue;
    //        Marker leaf_marker = seg.leafMarker;
    //        Marker root_marker = seg.root_marker;

    //        double sum_sig = 0;
    //        double sum_rdc = 0;

    //        Marker p = leaf_marker;
    //        while (p != root_marker.parent)
    //        {
    //            if (tmpimg[p.img_index(sz0, sz01)] == 0)
    //            {
    //                sum_rdc += img[p.img_index(sz0, sz01)];
    //            }
    //            else
    //            {
    //                int r = (int)p.radius;
    //                int x = (int)(p.position.x);
    //                int y = (int)(p.position.y);
    //                int z = (int)(p.position.z);
    //                double sum_sphere_size = 0;
    //                double sum_delete_size = 0;
    //                for (int ii = -r; ii <= r; ii++)
    //                {
    //                    int x2 = x + ii;
    //                    if (x2 < 0 || x2 >= sz0) continue;
    //                    for (int jj = -r; jj <= r; jj++)
    //                    {
    //                        int y2 = y + jj;
    //                        if (y2 < 0 || y2 >= sz1) continue;
    //                        for (int kk = -r; kk <= r; kk++)
    //                        {
    //                            int z2 = z + kk;
    //                            if (z2 < 0 || z2 >= sz2) continue;
    //                            if (ii * ii + jj * jj + kk * kk > r * r) continue;
    //                            int index = z2 * sz01 + y2 * sz0 + x2;
    //                            sum_sphere_size++;
    //                            if (tmpimg[index] != img[index])
    //                            {
    //                                sum_delete_size++;
    //                            }
    //                        }
    //                    }
    //                }

    //                if (sum_sphere_size > 0 && sum_delete_size / sum_sphere_size > 0.1)
    //                {
    //                    sum_rdc += img[p.img_index(sz0, sz01)];
    //                }
    //                else sum_sig += img[p.img_index(sz0, sz01)];
    //            }
    //            p = p.parent;
    //        }

    //        if (seg.parent == null || sum_rdc == 0 || (sum_sig / sum_rdc >= SR_ratio && sum_sig >= byte.MaxValue))
    //        {
    //            tol_sum_sig += sum_sig;
    //            tol_sum_rdc += sum_rdc;
    //            List<Marker> seg_markers = new List<Marker>();
    //            p = leaf_marker;
    //            while (p != root_marker)
    //            {
    //                if (tmpimg[p.img_index(sz0, sz01)] != 0)
    //                {
    //                    seg_markers.Add(p);
    //                }
    //                p = p.parent;
    //            }

    //            foreach (var marker in seg_markers)
    //            {
    //                p = marker;
    //                int r = (int)p.radius;
    //                if (r > 0)
    //                {
    //                    int x = (int)(p.position.x + 0.5);
    //                    int y = (int)(p.position.y + 0.5);
    //                    int z = (int)(p.position.z + 0.5);
    //                    double sum_sphere_size = 0;
    //                    double sum_delete_size = 0;
    //                    for (int ii = -r; ii <= r; ii++)
    //                    {
    //                        int x2 = x + ii;
    //                        if (x2 < 0 || x2 >= sz0) continue;
    //                        for (int jj = -r; jj <= r; jj++)
    //                        {
    //                            int y2 = y + jj;
    //                            if (y2 < 0 || y2 >= sz1) continue;
    //                            for (int kk = -r; kk <= r; kk++)
    //                            {
    //                                int z2 = z + kk;
    //                                if (z2 < 0 || z2 >= sz2) continue;
    //                                if (ii * ii + jj * jj + kk * kk > r * r) continue;
    //                                int index = z2 * sz01 + y2 * sz0 + x2;
    //                                tmpimg[index] = 0;
    //                            }

    //                        }
    //                    }
    //                }
    //            }

    //            out_segs.Add(seg);
    //            visited_segs.Add(seg);
    //        }
    //    }

    //    //evaluation
    //    double tree_sig = 0;
    //    double covered_sig = 0;
    //    foreach (var m in inswc)
    //    {
    //        tree_sig += img[m.img_index(sz0, sz01)];
    //        if (tmpimg[m.img_index(sz0, sz01)] == 0) covered_sig += img[m.img_index(sz0, sz01)];
    //    }
    //    //for (int i = 0; i < tol_sz; i++)
    //    //{
    //    //    if (tmpimg[i] == 0) covered_sig += img[i];
    //    //}
    //    Debug.Log("S/T ratio" + covered_sig / tree_sig + "(" + covered_sig + "/" + tree_sig + ")");
    //    //Debug.Log(out_segs.Count);

    //    //topo_segs2swc(out_segs, out outswc, 0);
    //    //Debug.Log(outswc.Count);
    //    //out_segs = Resample(out_segs, 10);
    //    List<Marker> outswc = TopoSegs2swc(out_segs, 0);
    //    return outswc;
    //    //topo_segs2swc(visited_segs,filter_segs, out outswc, 0);
    //}

    public List<Marker> Resample(List<Marker> inswc, byte[] img, int sz0, int sz1, int sz2,int factor =10)
    {
        int sz01 = sz0 * sz1;
        int tol_sz = sz01 * sz2;
        List<HierarchySegment> topo_segs = swc2topoSegs(inswc, img, sz0, sz1, sz2);
        topo_segs = Resample(topo_segs, factor);
        List<Marker> outswc = TopoSegs2swc(topo_segs, 0);
        //topo_segs2swc(visited_segs,filter_segs, out outswc, 0);
        return outswc;
    }

    public List<HierarchySegment> Resample(List<HierarchySegment> in_segs, int factor)
    {
        foreach (var seg in in_segs)
        {
            if (seg.root_marker.parent != null) seg.root_marker.parent.isSegment_root = true;
        }
        foreach (var seg in in_segs)
        {
            Marker marker = seg.leafMarker;
            Marker leafMarker = seg.leafMarker;
            Marker rootMarker = seg.root_marker;
            marker.isLeaf = true;
            while (marker != seg.root_marker)
            {
                double length = 0;
                Marker pre_marker = marker;
                int count_marker = 0;
                while (marker != seg.root_marker)
                {
                    length += Vector3.Distance(marker.position, marker.parent.position);
                    count_marker++;
                    marker = marker.parent;
                    if (marker.isSegment_root) break;
                }

                int count = count_marker / factor;
                double step = length / count;

                while (pre_marker != marker)
                {
                    Marker temp_marker = pre_marker;
                    double distance = 0;
                    while (distance < step && temp_marker != marker)
                    {
                        distance += Vector3.Distance(temp_marker.position, temp_marker.parent.position);
                        temp_marker = temp_marker.parent;
                    }
                    //double ratio = (distance - step) / Vector3.Distance(temp_marker.position, temp_marker.parent.position);
                    //Vector3 direction = temp_marker.parent.position - temp_marker.position;
                    //var new_maker = new Marker(temp_marker.position + (float)ratio * direction);
                    //new_maker.radius = (float)ratio * temp_marker.radius + (float)(1 - ratio) * temp_marker.parent.radius;
                    //new_maker.parent = temp_marker.parent;
                    pre_marker.parent = temp_marker;
                    pre_marker = temp_marker;
                }
                marker.isBranch_root = true;
            }
        }

        //foreach (var seg in in_segs)
        //{
        //    var leafMarker = seg.leafMarker;
        //    var root_marker = seg.root_marker;
        //    var marker = leafMarker;
        //    var pre_marker = leafMarker;
        //    float angle_sum=0;
        //    while (marker.parent != root_marker)
        //    {
        //        float angle = Vector3.Angle(marker.parent.position- marker.position, marker.parent.parent.position - marker.parent.position);
        //        angle_sum += angle;
        //        if (angle_sum > 10 ||marker.parent.isSegment_root)
        //        {
        //            pre_marker.angle = angle_sum;
        //            angle_sum = 0;
        //            pre_marker.parent = marker.parent;
        //            pre_marker = marker.parent;
        //        }
        //        marker = marker.parent;
        //    }
        //    pre_marker.parent = marker.parent;
        //}
        return in_segs;
    }
}
